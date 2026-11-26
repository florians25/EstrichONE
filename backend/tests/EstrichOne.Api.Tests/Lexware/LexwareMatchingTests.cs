using EstrichOne.Api.Modules.Lexware;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>Dublettenprüfung zwischen EstrichONE-Kunden und Lexware-Kontakten: typische Schreibweisen und Grenzfälle.</summary>
public sealed class LexwareMatchingTests
{
    private static MatchParty Party(string name, bool company = true, string? email = null, string? phone = null, string? street = null,
        string? postalCode = null, string? number = null, string? vatId = null, string? key = null) =>
        new(key ?? Guid.NewGuid().ToString(), name, company, email is null ? [] : [email], phone is null ? [] : [phone], street, postalCode, null, number, vatId);

    [Theory]
    [InlineData("Müller Estrich GmbH & Co. KG", "MUELLER ESTRICH GMBH")]
    [InlineData("Bau Huber e.K.", "Bau Huber")]
    [InlineData("Bau  Huber  e. K.", "bau huber")]
    [InlineData("Wohnbau Süd AG", "Wohnbau Sued")]
    [InlineData("Firma Schmidt Bauträger UG (haftungsbeschränkt)", "Schmidt Bauträger")]
    public void Company_SpellingAndLegalForm_AreIgnored(string a, string b)
    {
        var match = LexwareMatching.Compare(Party(a, postalCode: "83022"), Party(b, postalCode: "83022"));
        match.ShouldNotBeNull();
        match.Reasons.ShouldContain(LexwareMatching.SameNameAndPostalCode);
        match.Score.ShouldBeGreaterThanOrEqualTo(80);

        // Ohne PLZ bleibt „gleicher Name“ als schwächerer Treffer.
        LexwareMatching.Compare(Party(a), Party(b))!.Reasons.ShouldBe([LexwareMatching.SameName]);
    }

    [Theory]
    [InlineData("Müller, Hans", "Herr Hans Müller")]
    [InlineData("Familie Hans Müller", "Hans Mueller")]
    [InlineData("Dr. Anna-Lena Gruber", "Gruber Anna Lena")]
    public void Person_SalutationTitleAndOrder_AreIgnored(string a, string b)
    {
        var match = LexwareMatching.Compare(Party(a, company: false), Party(b, company: false));
        match.ShouldNotBeNull();
        match.Reasons.ShouldContain(LexwareMatching.SameName);
    }

    [Fact]
    public void SameEmail_IsStrong_RegardlessOfCaseAndName()
    {
        var match = LexwareMatching.Compare(Party("Huber Bau", email: "Info@Huber-Bau.de"), Party("H. B. Verwaltung", email: " info@huber-bau.de "));
        match.ShouldNotBeNull();
        match.Strong.ShouldBeTrue();
        match.Reasons.ShouldBe([LexwareMatching.SameEmail]);
    }

    [Theory]
    [InlineData("+49 171 2345678", "0171 / 234 56 78")]
    [InlineData("0049 8031 123456", "08031-123456")]
    [InlineData("(08031) 12 34 56", "+49 8031 123456")]
    public void SamePhone_IsRecognisedAcrossFormats(string a, string b)
    {
        var match = LexwareMatching.Compare(Party("Maier", company: false, phone: a), Party("Familie Maier-Schulz", company: false, phone: b));
        match.ShouldNotBeNull();
        match.Reasons.ShouldContain(LexwareMatching.SamePhone);
        match.Strong.ShouldBeFalse();
    }

    [Fact]
    public void PrivatePersonAndCompany_WithSameName_AreOnlyAWeakHint()
    {
        var person = Party("Hans Müller", company: false, postalCode: "83022");
        var company = Party("Hans Müller GmbH", company: true, postalCode: "83022");
        var match = LexwareMatching.Compare(person, company);
        match.ShouldNotBeNull();
        match.Strong.ShouldBeFalse();
        match.Reasons.ShouldContain(LexwareMatching.DifferentKind);
        match.Score.ShouldBeLessThan(LexwareMatching.Compare(person, Party("Hans Müller", company: false, postalCode: "83022"))!.Score);

        // Ohne PLZ reicht der gleiche Name zwischen Person und Firma gerade noch als Hinweis.
        LexwareMatching.Compare(Party("Hans Müller", company: false), Party("Hans Müller GmbH"))!.Score.ShouldBe(40);
    }

    [Fact]
    public void SameAddress_WithDifferentName_IsNoDuplicate()
    {
        LexwareMatching.Compare(Party("Bauträger Inntal GmbH", street: "Bahnhofstr. 12", postalCode: "83022"),
            Party("Familie Obermaier", company: false, street: "Bahnhofstraße 12", postalCode: "83022")).ShouldBeNull();

        // Kommt ein zweites Merkmal dazu, wird es ein Treffer mit beiden Gründen.
        var match = LexwareMatching.Compare(Party("Bauträger Inntal GmbH", street: "Bahnhofstr. 12", postalCode: "83022", phone: "08031 4455"),
            Party("Hausverwaltung Süd", street: "Bahnhofstraße 12", postalCode: "83022", phone: "+49 8031 4455"));
        match.ShouldNotBeNull();
        match.Reasons.ShouldBe([LexwareMatching.SamePhone, LexwareMatching.SameAddress]);
    }

    [Fact]
    public void SameCustomerNumber_CountsAsStrong_OnlyWithMatchingName()
    {
        // Unabhängige Nummernkreise: zufällig gleiche Nummer bei anderem Namen ist nur ein Hinweis.
        var weak = LexwareMatching.Compare(Party("Huber Bau GmbH", number: "10042"), Party("Estrich Schmid", number: "10042"));
        weak.ShouldNotBeNull();
        weak.Strong.ShouldBeFalse();
        weak.Reasons.ShouldBe([LexwareMatching.SameCustomerNumber]);

        var strong = LexwareMatching.Compare(Party("Huber Bau GmbH", number: "10042"), Party("Huber Bau", number: "10042"));
        strong.ShouldNotBeNull();
        strong.Strong.ShouldBeTrue();
        LexwareMatching.Compare(Party("Huber Bau GmbH", number: "K-10042"), Party("Zimmerei Lang", number: "10042")).ShouldBeNull();
    }

    [Fact]
    public void SameVatId_IsStrong_IgnoringSpaces()
    {
        var match = LexwareMatching.Compare(Party("Alt GmbH", vatId: "DE 123 456 789"), Party("Neu Holding", vatId: "de123456789"));
        match.ShouldNotBeNull();
        match.Strong.ShouldBeTrue();
        match.Reasons.ShouldBe([LexwareMatching.SameVatId]);
    }

    [Fact]
    public void SimilarNames_TypoOrContainedName_AreAHint_SharedWordAloneNeedsPostalCode()
    {
        // Tippfehler bzw. Namenszusatz: alle Teile des kürzeren Namens kommen im anderen vor.
        LexwareMatching.Compare(Party("Schnieder Bau"), Party("Schneider Bau"))!.Reasons.ShouldBe([LexwareMatching.SimilarName]);
        LexwareMatching.Compare(Party("Huber Bau Zweigstelle"), Party("Huber Bau GmbH"))!.Reasons.ShouldBe([LexwareMatching.SimilarName]);

        // Nur ein gemeinsames Wort („Bau“): erst zusammen mit gleicher PLZ ein Hinweis.
        LexwareMatching.Compare(Party("Bau Müller"), Party("Bau Schmidt")).ShouldBeNull();
        LexwareMatching.Compare(Party("Bau Müller", postalCode: "80331"), Party("Bau Schmidt", postalCode: "80331"))!.Reasons
            .ShouldBe([LexwareMatching.SimilarName]);
        LexwareMatching.Compare(Party("Hans Müller", company: false), Party("Müller", company: false)).ShouldBeNull();
    }

    [Fact]
    public void UnrelatedEntries_DoNotMatch()
    {
        LexwareMatching.Compare(Party("Huber Bau GmbH", email: "a@huber.de", phone: "08031 1111", postalCode: "83022"),
            Party("Estrich Schmid", email: "b@schmid.de", phone: "089 2222222", postalCode: "80331")).ShouldBeNull();
        LexwareMatching.Compare(Party("GmbH"), Party("GmbH & Co. KG")).ShouldBeNull(); // nur Rechtsform, kein Name
        LexwareMatching.Compare(Party("Maier", company: false, phone: "123"), Party("Huber", company: false, phone: "123")).ShouldBeNull(); // zu kurz
    }

    [Fact]
    public void Find_OrdersBestFirst_AndSkipsTheSubjectItself()
    {
        var subject = Party("Huber Bau GmbH", email: "info@huber-bau.de", postalCode: "83022", key: "self");
        var pool = new[]
        {
            subject,
            Party("Huber Bau", postalCode: "83022", key: "name"),
            Party("Irgendwer", email: "info@huber-bau.de", key: "mail"),
            Party("Huber Bau GmbH", email: "info@huber-bau.de", postalCode: "83022", key: "both"),
            Party("Ganz Anders AG", key: "none"),
        };
        LexwareMatching.Find(subject, pool).Select(r => r.Party.Key).ShouldBe(["both", "mail", "name"]);
    }

    [Fact]
    public void Pairs_FindsCrossAndInternalDuplicates()
    {
        var local = new[]
        {
            Party("Huber Bau GmbH", email: "info@huber-bau.de", key: "c1"), Party("Estrich Schmid", postalCode: "80331", key: "c2"),
            Party("Estrich Schmid e.K.", postalCode: "80331", key: "c3"), Party("Einzelgänger", key: "c4"),
        };
        var remote = new[] { Party("Huber", email: "INFO@huber-bau.de", key: "l1"), Party("Lang Zimmerei", key: "l2") };
        LexwareMatching.Pairs(local, remote).Select(p => (p.Left.Key, p.Right.Key)).ShouldBe([("c1", "l1")]);
        LexwareMatching.PairsWithin(local).Select(p => (p.Left.Key, p.Right.Key)).ShouldBe([("c2", "c3")]);
    }

    [Fact]
    public void Normalisation_Helpers()
    {
        LexwareMatching.NameTokens("Müller Estrich GmbH & Co. KG").ShouldBe(["mueller", "estrich"]);
        LexwareMatching.Normalize("Bahnhofstr. 12a").ShouldBe("bahnhofstrasse 12a");
        LexwareMatching.Normalize("Café René").ShouldBe("cafe rene");
        LexwareMatching.PhoneKey("+49 (171) 234-5678").ShouldBe("12345678");
        LexwareMatching.PhoneKey("12 34").ShouldBeNull();
    }
}
