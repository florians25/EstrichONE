import Foundation
#if canImport(SQLite3)
import SQLite3
#else
import CSQLite
#endif

/// Wert einer SQLite-Spalte.
public enum SQLValue: Equatable, Sendable {
    case null
    case integer(Int64)
    case real(Double)
    case text(String)
    case blob(Data)

    public var string: String? { if case let .text(value) = self { value } else { nil } }
    public var int: Int64? {
        switch self {
        case let .integer(value): value
        case let .real(value): Int64(value)
        default: nil
        }
    }
}

extension SQLValue: ExpressibleByStringLiteral, ExpressibleByIntegerLiteral, ExpressibleByNilLiteral {
    public init(stringLiteral value: String) { self = .text(value) }
    public init(integerLiteral value: Int64) { self = .integer(value) }
    public init(nilLiteral: ()) { self = .null }

    public init(_ value: String?) { self = value.map(SQLValue.text) ?? .null }
    public init(_ value: Int?) { self = value.map { .integer(Int64($0)) } ?? .null }
}

public typealias SQLRow = [String: SQLValue]

public struct SQLiteError: Error, Equatable, CustomStringConvertible {
    public let code: Int32
    public let message: String
    public var description: String { "SQLite \(code): \(message)" }
}

/// Schlanke Hülle um eine SQLite-Verbindung im Arbeitsspeicher (ADR 0013). Nicht threadsicher:
/// Zugriff nur aus einem Actor (`LocalStore`).
public final class SQLiteDatabase {
    private var handle: OpaquePointer?
    private static let transient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

    /// Leere Datenbank im Arbeitsspeicher oder aus einem serialisierten Abbild.
    public init(image: Data? = nil) throws {
        guard sqlite3_open_v2(":memory:", &handle, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_NOMUTEX, nil) == SQLITE_OK else {
            throw SQLiteError(code: SQLITE_CANTOPEN, message: "Datenbank kann nicht geöffnet werden.")
        }
        if let image, !image.isEmpty {
            try load(image)
        }
        try execute("PRAGMA foreign_keys = ON")
    }

    deinit {
        sqlite3_close_v2(handle)
    }

    /// Abbild der gesamten Datenbank (zum verschlüsselten Speichern).
    public func serialize() throws -> Data {
        var size: sqlite3_int64 = 0
        guard let pointer = sqlite3_serialize(handle, "main", &size, 0) else {
            throw lastError()
        }
        defer { sqlite3_free(pointer) }
        return Data(bytes: pointer, count: Int(size))
    }

    private func load(_ image: Data) throws {
        guard let buffer = sqlite3_malloc64(UInt64(image.count)) else {
            throw SQLiteError(code: SQLITE_NOMEM, message: "Zu wenig Arbeitsspeicher.")
        }
        let bytes = buffer.assumingMemoryBound(to: UInt8.self)
        image.copyBytes(to: bytes, count: image.count)
        let flags = UInt32(SQLITE_DESERIALIZE_FREEONCLOSE | SQLITE_DESERIALIZE_RESIZEABLE)
        let result = sqlite3_deserialize(handle, "main", bytes, sqlite3_int64(image.count), sqlite3_int64(image.count), flags)
        guard result == SQLITE_OK else {
            throw SQLiteError(code: result, message: "Das Datenbankabbild ist beschädigt.")
        }
    }

    @discardableResult
    public func execute(_ sql: String, _ arguments: [SQLValue] = []) throws -> Int {
        let statement = try prepare(sql, arguments)
        defer { sqlite3_finalize(statement) }
        var result = sqlite3_step(statement)
        while result == SQLITE_ROW {
            result = sqlite3_step(statement)
        }
        guard result == SQLITE_DONE else { throw lastError() }
        return Int(sqlite3_changes(handle))
    }

    public func query(_ sql: String, _ arguments: [SQLValue] = []) throws -> [SQLRow] {
        let statement = try prepare(sql, arguments)
        defer { sqlite3_finalize(statement) }
        var rows: [SQLRow] = []
        while true {
            let result = sqlite3_step(statement)
            if result == SQLITE_DONE { break }
            guard result == SQLITE_ROW else { throw lastError() }
            var row: SQLRow = [:]
            for index in 0..<sqlite3_column_count(statement) {
                let name = String(cString: sqlite3_column_name(statement, index))
                row[name] = column(statement, index)
            }
            rows.append(row)
        }
        return rows
    }

    public func scalar(_ sql: String, _ arguments: [SQLValue] = []) throws -> SQLValue {
        try query(sql, arguments).first?.values.first ?? .null
    }

    /// Führt `work` in einer Transaktion aus; bei einem Fehler wird alles zurückgerollt.
    @discardableResult
    public func transaction<T>(_ work: () throws -> T) throws -> T {
        try execute("BEGIN IMMEDIATE")
        do {
            let result = try work()
            try execute("COMMIT")
            return result
        } catch {
            _ = try? execute("ROLLBACK")
            throw error
        }
    }

    public var userVersion: Int {
        get { Int((try? scalar("PRAGMA user_version").int) ?? 0) }
        set { _ = try? execute("PRAGMA user_version = \(newValue)") }
    }

    private func prepare(_ sql: String, _ arguments: [SQLValue]) throws -> OpaquePointer? {
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK else { throw lastError() }
        for (offset, value) in arguments.enumerated() {
            let index = Int32(offset + 1)
            let result: Int32 = switch value {
            case .null: sqlite3_bind_null(statement, index)
            case let .integer(number): sqlite3_bind_int64(statement, index, number)
            case let .real(number): sqlite3_bind_double(statement, index, number)
            case let .text(text): sqlite3_bind_text(statement, index, text, -1, Self.transient)
            case let .blob(data): data.withUnsafeBytes { sqlite3_bind_blob(statement, index, $0.baseAddress, Int32($0.count), Self.transient) }
            }
            guard result == SQLITE_OK else {
                sqlite3_finalize(statement)
                throw lastError()
            }
        }
        return statement
    }

    private func column(_ statement: OpaquePointer?, _ index: Int32) -> SQLValue {
        switch sqlite3_column_type(statement, index) {
        case SQLITE_INTEGER: .integer(sqlite3_column_int64(statement, index))
        case SQLITE_FLOAT: .real(sqlite3_column_double(statement, index))
        case SQLITE_TEXT: .text(String(cString: sqlite3_column_text(statement, index)))
        case SQLITE_BLOB:
            if let bytes = sqlite3_column_blob(statement, index) {
                .blob(Data(bytes: bytes, count: Int(sqlite3_column_bytes(statement, index))))
            } else {
                .blob(Data())
            }
        default: .null
        }
    }

    private func lastError() -> SQLiteError {
        SQLiteError(code: sqlite3_errcode(handle), message: String(cString: sqlite3_errmsg(handle)))
    }
}
