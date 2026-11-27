using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Lexware;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>Dreiwege-Abgleich verknüpfter Kunden als reine Funktion: wer hat was geändert, was wird übernommen, was bleibt Abweichung.</summary>
public sealed class LexwareContactSyncTests
{
    private static Dictionary<string, string?> Fields(string? name = "Huber Bau GmbH", string? street = "Altweg 1", string? postalCode = "83022",
        string? city = "Rosenheim", string? email = "info@huber.example", string? phone = "08031 1111", string? vatId = "DE111111111") => new()
    {
        [LexwareSyncFields.Name] = name, [LexwareSyncFields.Street] = street, [LexwareSyncFields.PostalCode] = postalCode, [LexwareSyncFields.City] = city,
        [LexwareSyncFields.Email] = email, [LexwareSyncFields.Phone] = phone, [LexwareSyncFields.VatId] = vatId,
    };

    [Fact]
    public void EqualSides_NeedNothing_AndBecomeTheBaseline()
    {
        var result = LexwareContactSync.Merge(null, Fields(), Fields(), LexwareChangeSync.Both);
        result.ToLexware.ShouldBeEmpty();
        result.ToEstrichOne.ShouldBeEmpty();
        result.Deviations.ShouldBeEmpty();
        result.Baseline[LexwareSyncFields.Street].ShouldBe("Altweg 1");
        result.Baseline.Count.ShouldBe(7);
    }

    [Fact]
    public void FirstSync_NeverOverwrites_DifferencesBecomeDeviations()
    {
        var result = LexwareContactSync.Merge(null, Fields(street: "Neuweg 2"), Fields(city: "Kolbermoor"), LexwareChangeSync.Both);
        result.ToLexware.ShouldBeEmpty();
        result.ToEstrichOne.ShouldBeEmpty();
        result.Deviations.ShouldBe([new LexwareFieldDeviation(LexwareSyncFields.Street, "Neuweg 2", "Altweg 1"),
            new LexwareFieldDeviation(LexwareSyncFields.City, "Rosenheim", "Kolbermoor")]);
        // Für Felder mit Abweichung gibt es weiter keinen abgeglichenen Stand.
        result.Baseline.ContainsKey(LexwareSyncFields.Street).ShouldBeFalse();
        result.Baseline[LexwareSyncFields.Name].ShouldBe("Huber Bau GmbH");
    }

    [Fact]
    public void OnlyOneSideChanged_IsTakenOver_PerField()
    {
        var result = LexwareContactSync.Merge(Fields(), Fields(street: "Neuweg 2", phone: null), Fields(email: "buero@huber.example"), LexwareChangeSync.Both);
        result.ToLexware.ShouldBe(new Dictionary<string, string?> { [LexwareSyncFields.Street] = "Neuweg 2", [LexwareSyncFields.Phone] = null });
        result.ToEstrichOne.ShouldBe(new Dictionary<string, string?> { [LexwareSyncFields.Email] = "buero@huber.example" });
        result.Deviations.ShouldBeEmpty();
        (result.Baseline[LexwareSyncFields.Street], result.Baseline[LexwareSyncFields.Phone], result.Baseline[LexwareSyncFields.Email])
            .ShouldBe(("Neuweg 2", null, "buero@huber.example"));
    }

    [Fact]
    public void BothSidesChangedTheSameField_IsAConflict_NothingIsOverwritten()
    {
        var result = LexwareContactSync.Merge(Fields(), Fields(street: "Neuweg 2", city: "Kolbermoor"), Fields(street: "Neuer Weg 2", city: "Kolbermoor"),
            LexwareChangeSync.Both);
        result.ToLexware.ShouldBeEmpty();
        result.ToEstrichOne.ShouldBeEmpty();
        result.Deviations.ShouldBe([new LexwareFieldDeviation(LexwareSyncFields.Street, "Neuweg 2", "Neuer Weg 2")]);
        result.Baseline[LexwareSyncFields.Street].ShouldBe("Altweg 1"); // bleibt, bis der Nutzer entschieden hat
        result.Baseline[LexwareSyncFields.City].ShouldBe("Kolbermoor"); // beide gleich geändert: kein Konflikt
    }

    [Theory]
    [InlineData(LexwareChangeSync.Both, true, true)]
    [InlineData(LexwareChangeSync.ToLexware, true, false)]
    [InlineData(LexwareChangeSync.FromLexware, false, true)]
    [InlineData(LexwareChangeSync.Off, false, false)]
    public void Direction_LimitsWhatIsTakenOver_TheRestStaysVisibleAsDeviation(LexwareChangeSync direction, bool push, bool pull)
    {
        var result = LexwareContactSync.Merge(Fields(), Fields(street: "Neuweg 2"), Fields(email: "buero@huber.example"), direction);
        result.ToLexware.ContainsKey(LexwareSyncFields.Street).ShouldBe(push);
        result.ToEstrichOne.ContainsKey(LexwareSyncFields.Email).ShouldBe(pull);
        result.Deviations.Select(d => d.Field).ShouldBe(new[] { push ? null : LexwareSyncFields.Street, pull ? null : LexwareSyncFields.Email }.Where(f => f is not null)!);
    }

    [Fact]
    public void Choices_DecideDeviations_PerField_IndependentOfDirection()
    {
        var local = Fields(street: "Neuweg 2", city: "Kolbermoor", email: "a@huber.example");
        var remote = Fields(street: "Neuer Weg 2", city: "Bad Aibling", email: "b@huber.example");
        var result = LexwareContactSync.Merge(Fields(), local, remote, LexwareChangeSync.Off, new Dictionary<string, string>
        {
            [LexwareSyncFields.Street] = LexwareContactSync.EstrichOne, [LexwareSyncFields.City] = LexwareContactSync.Lexware,
        });
        result.ToLexware.ShouldBe(new Dictionary<string, string?> { [LexwareSyncFields.Street] = "Neuweg 2" });
        result.ToEstrichOne.ShouldBe(new Dictionary<string, string?> { [LexwareSyncFields.City] = "Bad Aibling" });
        result.Deviations.ShouldBe([new LexwareFieldDeviation(LexwareSyncFields.Email, "a@huber.example", "b@huber.example")]);
        (result.Baseline[LexwareSyncFields.Street], result.Baseline[LexwareSyncFields.City]).ShouldBe(("Neuweg 2", "Bad Aibling"));
    }

    [Fact]
    public void Name_IsNeverEmptied_AndOnlyFieldsOfTheLexwareContactAreCompared()
    {
        var remote = Fields(name: null);
        remote.Remove(LexwareSyncFields.VatId); // Person in Lexware Office: keine USt-ID
        var result = LexwareContactSync.Merge(Fields(), Fields(vatId: "DE999999999"), remote, LexwareChangeSync.Both);
        result.ToEstrichOne.ShouldBeEmpty();
        result.ToLexware.ShouldBeEmpty();
        result.Deviations.ShouldBe([new LexwareFieldDeviation(LexwareSyncFields.Name, "Huber Bau GmbH", null)]);
    }

    [Theory]
    [InlineData(LexwareSyncFields.Email, "Info@Huber.example", "info@huber.example", true)]
    [InlineData(LexwareSyncFields.Phone, "+49 8031 1111", "08031/1111", true)]
    [InlineData(LexwareSyncFields.Phone, "0049 8031 1111", "08031 1111", true)]
    [InlineData(LexwareSyncFields.Phone, "08031 1111", "08031 1112", false)]
    [InlineData(LexwareSyncFields.Street, " Altweg  1 ", "Altweg 1", true)]
    [InlineData(LexwareSyncFields.Street, "Altweg 1", "altweg 1", false)]
    [InlineData(LexwareSyncFields.VatId, "DE 111 111 111", "de111111111", true)]
    [InlineData(LexwareSyncFields.City, "", null, true)]
    [InlineData(LexwareSyncFields.City, "Rosenheim", null, false)]
    public void Same_IgnoresFormatting_NotContent(string field, string? left, string? right, bool same) =>
        LexwareContactSync.Same(field, left, right).ShouldBe(same);

    private static Contact Customer(ContactKind kind, string name)
    {
        var contact = Contact.Create(Guid.CreateVersion7(), kind, name);
        contact.Update(kind, name, "K-00012", "Altweg 1", "83022", "Rosenheim", "08031 1111", "info@huber.example", "Notiz");
        return contact;
    }

    [Fact]
    public void Local_UsesBillingAddressWhenPresent_AndPersonNamesAsFirstLast()
    {
        var person = Customer(ContactKind.Person, "Huber, Hans");
        person.UpdateDetails(new ContactDetails([new ContactPhone(ContactPhoneKind.Fax, "08031 9999"), new ContactPhone(ContactPhoneKind.Mobile, "0171 2222")],
            [new ContactEmail("hans@huber.example"), new ContactEmail("zweite@huber.example")], "Herr", null,
            new ContactAddress("Huber Verwaltung", "Postfach 7", "83001", "Rosenheim"), null, null, null, null, null, null));

        var local = LexwareContactSync.Local(person, lexwareIsCompany: false);

        local.ShouldBe(new Dictionary<string, string?>
        {
            [LexwareSyncFields.Name] = "Hans Huber", [LexwareSyncFields.Salutation] = "Herr", [LexwareSyncFields.Street] = "Postfach 7",
            [LexwareSyncFields.PostalCode] = "83001", [LexwareSyncFields.City] = "Rosenheim", [LexwareSyncFields.Email] = "hans@huber.example",
            [LexwareSyncFields.Phone] = "0171 2222", [LexwareSyncFields.VatId] = null,
        });
    }

    [Fact]
    public void Remote_PicksTheEntryThatBelongsToTheCustomer_AmongSeveral()
    {
        var contact = new LexwareContact("1", true, "Huber Bau GmbH", null, null, null, "10001", true, ["zentrale@huber.example", "info@huber.example"],
            ["08031 5555", "08031 1111"], "Lager 5", "83024", "Rosenheim", "DE111111111", null, false, 3, null, HasBillingAddress: false);
        var remote = LexwareContactSync.Remote(contact, Fields(), null);
        (remote[LexwareSyncFields.Email], remote[LexwareSyncFields.Phone]).ShouldBe(("info@huber.example", "08031 1111"));
        remote[LexwareSyncFields.Street].ShouldBeNull(); // nur eine Lieferanschrift: keine Rechnungsanschrift
        remote.ContainsKey(LexwareSyncFields.Salutation).ShouldBeFalse();

        // Kennt der Kunde keinen der Einträge, gilt der zuletzt abgeglichene, sonst der erste.
        LexwareContactSync.Remote(contact, Fields(email: "neu@huber.example"), Fields())[LexwareSyncFields.Email].ShouldBe("info@huber.example");
        LexwareContactSync.Remote(contact, Fields(email: "neu@huber.example"), null)[LexwareSyncFields.Email].ShouldBe("zentrale@huber.example");
    }

    [Fact]
    public void ApplyToCustomer_ChangesOnlyTheGivenFields_AndNeverTheCustomerNumber()
    {
        var company = Customer(ContactKind.Company, "Huber Bau GmbH");
        company.UpdateDetails(new ContactDetails([new ContactPhone(ContactPhoneKind.Office, "08031 1111", "Büro"), new ContactPhone(ContactPhoneKind.Mobile, "0171 2222")],
            [new ContactEmail("info@huber.example", "Zentrale"), new ContactEmail("zweite@huber.example")], null, null, null,
            new ContactAddress(null, "Lager 5", "83024", "Rosenheim"), "DE111111111", "156/123", null, null, "huber.example"));

        LexwareContactSync.ApplyToCustomer(company, new Dictionary<string, string?>
        {
            [LexwareSyncFields.Name] = "Huber Bau GmbH & Co. KG", [LexwareSyncFields.Street] = "Neuweg 2", [LexwareSyncFields.Email] = "zweite@huber.example",
            [LexwareSyncFields.Phone] = "08031 3333", [LexwareSyncFields.VatId] = null,
        });

        (company.Name, company.CustomerNumber, company.Street, company.PostalCode, company.City, company.Notes)
            .ShouldBe(("Huber Bau GmbH & Co. KG", "K-00012", "Neuweg 2", "83022", "Rosenheim", "Notiz"));
        company.Emails.ShouldBe([new ContactEmail("zweite@huber.example", "Zentrale")]);
        company.Email.ShouldBe("zweite@huber.example");
        company.Phones.ShouldBe([new ContactPhone(ContactPhoneKind.Office, "08031 3333", "Büro"), new ContactPhone(ContactPhoneKind.Mobile, "0171 2222")]);
        (company.VatId, company.TaxNumber, company.Website, company.DeliveryAddress!.Street).ShouldBe((null, "156/123", "huber.example", "Lager 5"));
        company.BillingAddress.ShouldBeNull();

        // Mit abweichender Rechnungsanschrift ändert sich diese, nicht die Anschrift des Kunden.
        company.UpdateDetails(new ContactDetails(company.Phones, company.Emails, null, null, new ContactAddress("Buchhaltung", "Postfach 7", "83001", "Rosenheim"),
            company.DeliveryAddress, null, null, null, null, null));
        LexwareContactSync.ApplyToCustomer(company, new Dictionary<string, string?> { [LexwareSyncFields.City] = "Kolbermoor" });
        company.BillingAddress.ShouldBe(new ContactAddress("Buchhaltung", "Postfach 7", "83001", "Kolbermoor"));
        (company.Street, company.City).ShouldBe(("Neuweg 2", "Rosenheim"));
    }
}
