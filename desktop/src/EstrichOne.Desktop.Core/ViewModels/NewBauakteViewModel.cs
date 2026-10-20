using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

/// <summary>Wie der Kunde der neuen Bauakte bestimmt wird.</summary>
public enum CustomerChoice { None, Existing, New }

/// <summary>
/// Bauakte anlegen – mit Kundensuche oder Kunden-Neuanlage im selben Dialog: Ist „Neuer Kunde“ gewählt, legt Speichern zuerst
/// den Kunden an (idempotent über die ID) und verknüpft ihn dann mit der neuen Bauakte. Optional übernimmt die Baustelle die Kundenadresse.
/// </summary>
public sealed partial class NewBauakteViewModel(AppSession session) : PageViewModel, IDisposable
{
    private readonly Guid _bauakteId = Guid.CreateVersion7();
    private readonly Guid _customerId = Guid.CreateVersion7();
    private CancellationTokenSource? _search;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>Als Anfrage anlegen (Status „Anfrage“) statt als Auftrag („Geplant“).</summary>
    [ObservableProperty]
    public partial bool AsInquiry { get; set; }

    [ObservableProperty]
    public partial string Street { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PostalCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string City { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CustomerChoice Choice { get; set; }

    // Vorhandener Kunde

    [ObservableProperty]
    public partial string CustomerSearch { get; set; } = string.Empty;

    public ObservableCollection<Contact> CustomerResults { get; } = [];

    [ObservableProperty]
    public partial Contact? SelectedCustomer { get; set; }

    // Neuer Kunde

    [ObservableProperty]
    public partial ContactKind NewCustomerKind { get; set; } = ContactKind.Person;

    [ObservableProperty]
    public partial string NewCustomerName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewCustomerNumber { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewCustomerPhone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewCustomerEmail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewCustomerStreet { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewCustomerPostalCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewCustomerCity { get; set; } = string.Empty;

    /// <summary>Baustelle = Adresse des neuen Kunden (häufig beim Privatkunden).</summary>
    [ObservableProperty]
    public partial bool SiteIsCustomerAddress { get; set; }

    public bool CanCreateCustomer => session.Client.Has("contacts.edit");

    public bool CanSearchCustomers => session.Client.Has("contacts.read");

    partial void OnSelectedCustomerChanged(Contact? value)
    {
        if (value is not null)
        {
            Choice = CustomerChoice.Existing;
        }
    }

    /// <summary>Kunden suchen (Name, Kundennummer, Ort) ab zwei Zeichen; ältere Suchen werden abgebrochen.</summary>
    public async Task SearchCustomersAsync()
    {
        // Ältere Suche nur abbrechen, nicht freigeben: Sie kann das Token noch verwenden.
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        var text = CustomerSearch.Trim();
        if (text.Length < 2)
        {
            CustomerResults.Clear();
            return;
        }

        try
        {
            var found = await session.Client.SearchContactsAsync(text, 15, token);
            if (!token.IsCancellationRequested)
            {
                CustomerResults.Clear();
                foreach (var contact in found)
                {
                    CustomerResults.Add(contact);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Neue Eingabe.
        }
        catch (ApiException exception)
        {
            Error = exception.Message;
        }
    }

    /// <summary>Prüft die Eingaben; <c>null</c> = in Ordnung, sonst die Meldung für die Oberfläche.</summary>
    public string? Validate() => (Choice, Title) switch
    {
        (_, var t) when string.IsNullOrWhiteSpace(t) => "Bitte das Bauvorhaben angeben.",
        (CustomerChoice.Existing, _) when SelectedCustomer is null => "Bitte einen Kunden auswählen oder „Ohne Kunde“ wählen.",
        (CustomerChoice.New, _) when string.IsNullOrWhiteSpace(NewCustomerName) => "Bitte den Namen des neuen Kunden angeben.",
        _ => null,
    };

    /// <summary>Kunde (falls neu) und Bauakte anlegen. Liefert die neue Bauakte oder <c>null</c> (Fehler in <see cref="PageViewModel.Error"/>).</summary>
    public async Task<BauakteDetail?> CreateAsync()
    {
        if (Validate() is { } problem)
        {
            Error = problem;
            return null;
        }

        BauakteDetail? created = null;
        await RunAsync(async () =>
        {
            var client = session.Client;
            Guid? customerId = Choice switch
            {
                CustomerChoice.Existing => SelectedCustomer!.Id,
                CustomerChoice.New => (await client.CreateContactAsync(new SaveContactRequest(_customerId, NewCustomerKind, NewCustomerName.Trim(),
                    Blank(NewCustomerNumber), Blank(NewCustomerStreet), Blank(NewCustomerPostalCode), Blank(NewCustomerCity), Blank(NewCustomerPhone),
                    Blank(NewCustomerEmail)))).Id,
                _ => null,
            };
            var copyAddress = Choice == CustomerChoice.New && SiteIsCustomerAddress;
            created = await client.CreateBauakteAsync(new CreateBauakteRequest(_bauakteId, Title.Trim(),
                copyAddress ? Blank(NewCustomerStreet) : Blank(Street), copyAddress ? Blank(NewCustomerPostalCode) : Blank(PostalCode),
                copyAddress ? Blank(NewCustomerCity) : Blank(City), Description: Blank(Description), CustomerId: customerId,
                Status: AsInquiry ? BauakteStatus.Inquiry : BauakteStatus.Planned));
        });
        return created;
    }

    public void Dispose() => _search?.Dispose();

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
