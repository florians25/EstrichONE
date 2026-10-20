package de.estrichone.core

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// Datentypen der API v1 (contracts/openapi/v1.json). JSON: camelCase, Aufzählungen als camelCase-Text.

@Serializable
data class ServerInfo(
    val product: String,
    val serverName: String,
    val serverVersion: String,
    val apiVersions: List<String> = emptyList(),
    val minClientVersion: String = "0.0.0",
    val authMethods: List<String> = emptyList(),
    val setupRequired: Boolean = false,
)

@Serializable
data class UserSummary(val id: String, val displayName: String, val userName: String, val email: String? = null)

@Serializable
data class TokenResponse(
    val accessToken: String,
    val accessTokenExpiresAt: String,
    val refreshToken: String,
    val refreshTokenExpiresAt: String,
    val user: UserSummary,
    val mustChangePassword: Boolean = false,
)

@Serializable
data class RefreshResponse(val accessToken: String, val accessTokenExpiresAt: String, val refreshToken: String, val refreshTokenExpiresAt: String)

@Serializable
enum class DeviceKind { @SerialName("personal") Personal, @SerialName("pool") Pool }

@Serializable
enum class DeviceStatus { @SerialName("pending") Pending, @SerialName("active") Active, @SerialName("locked") Locked, @SerialName("retired") Retired }

@Serializable
data class DeviceResponse(
    val id: String,
    val name: String,
    val kind: DeviceKind,
    val status: DeviceStatus,
    val model: String? = null,
    val osVersion: String? = null,
    val appVersion: String? = null,
)

@Serializable
enum class BauakteStatus {
    @SerialName("inquiry") Inquiry,
    @SerialName("planned") Planned,
    @SerialName("inProgress") InProgress,
    @SerialName("drying") Drying,
    @SerialName("completed") Completed,
    @SerialName("cancelled") Cancelled;

    val label: String
        get() = when (this) {
            Inquiry -> "Anfrage"
            Planned -> "Geplant"
            InProgress -> "In Ausführung"
            Drying -> "Belegreif / Trocknung"
            Completed -> "Abgeschlossen"
            Cancelled -> "Storniert"
        }
}

@Serializable
data class BauakteListItem(
    val id: String,
    val number: String? = null,
    val title: String,
    val status: BauakteStatus,
    val street: String? = null,
    val postalCode: String? = null,
    val city: String? = null,
    val updatedAt: String,
)

@Serializable
data class BauaktePage(val items: List<BauakteListItem>, val nextCursor: String? = null)

@Serializable
data class BauakteDetail(
    val id: String,
    val number: String? = null,
    val title: String,
    val status: BauakteStatus,
    val street: String? = null,
    val postalCode: String? = null,
    val city: String? = null,
    val latitude: Double? = null,
    val longitude: Double? = null,
    val description: String? = null,
    val customerId: String? = null,
    val responsibleUserId: String? = null,
    val siteContactName: String? = null,
    val siteContactPhone: String? = null,
    val siteNotes: String? = null,
    val version: Long,
)

@Serializable
data class CreateBauakteRequest(
    val id: String,
    val title: String,
    val street: String? = null,
    val postalCode: String? = null,
    val city: String? = null,
    val description: String? = null,
)

@Serializable
enum class HintSeverity { @SerialName("info") Info, @SerialName("success") Success, @SerialName("warning") Warning, @SerialName("danger") Danger }

@Serializable
data class BauakteHint(val kind: String, val severity: HintSeverity, val text: String)

@Serializable
data class Note(val id: String, val bauakteId: String, val authorUserId: String, val text: String, val isPinned: Boolean = false, val createdAt: String)

@Serializable
enum class WorkItemKind { @SerialName("task") Task, @SerialName("defect") Defect }

@Serializable
enum class WorkItemStatus { @SerialName("open") Open, @SerialName("inProgress") InProgress, @SerialName("done") Done }

@Serializable
data class WorkItem(
    val id: String,
    val bauakteId: String? = null,
    val kind: WorkItemKind,
    val status: WorkItemStatus,
    val title: String,
    val dueDate: String? = null,
)

@Serializable
data class AssignmentPerson(val id: String, val name: String)

@Serializable
data class Assignment(
    val id: String,
    val day: String,
    val startTime: String? = null,
    val endTime: String? = null,
    val activity: String,
    val bauakteId: String,
    val bauakteNumber: String? = null,
    val bauakteTitle: String,
    val bauakteAddress: String? = null,
    val crewName: String? = null,
    val people: List<AssignmentPerson> = emptyList(),
    val notes: String? = null,
    val warnings: List<String> = emptyList(),
)

@Serializable
data class PlanningRange(val from: String, val to: String, val assignments: List<Assignment>)

@Serializable
data class DirectoryUser(val id: String, val displayName: String, val isActive: Boolean = true)

@Serializable
data class TicketSummary(val id: String, val number: String, val title: String, val status: String)

@Serializable
data class TicketDetail(val summary: TicketSummary)

/** Fehlerantwort nach RFC 9457 mit stabilem `code`. */
@Serializable
internal data class Problem(val title: String? = null, val detail: String? = null, val code: String? = null)
