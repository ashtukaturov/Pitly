namespace Pitly.Core.Models;

/// <summary>
/// Field numbers (poz.) of the PIT-38 form version that applies to a tax year. Only versions that were checked
/// field by field against the form published by the Ministry of Finance are listed; for any other year
/// <see cref="ForYear"/> returns null and the amounts are shown without field numbers.
///
/// Sources (form PDFs and the "Broszura informacyjna do zeznania PIT-38"):
///   PIT-38(18), tax year 2025: https://www.gov.pl/attachment/1874d956-4063-4a9c-a65c-ab7b6bc7aa30
///   PIT-38(17), tax year 2024: https://www.gov.pl/attachment/17d2cf10-3b57-4f7a-b37c-5502fdb92899
///   PIT-38(16), tax years 2022-2023: https://www.gov.pl/attachment/ae98d122-2810-4388-9316-2ac156528945
///   PIT-38(15), tax year 2021: https://podatki-arch.mf.gov.pl/media/7502/pit-38_15-08.pdf
///   Version per year: https://podatki-arch.mf.gov.pl/pit/formularze-do-druku-pit/
///
/// Versions (15), (16) and (17) number these fields identically. Version (18) added row 3 in section C
/// (exempt income under art. 21 ust. 1 pkt 105a, poz. 24-25), which moved "Razem" and every later field by two.
/// </summary>
public record Pit38FormLayout(
    int Version,
    // Section C, row 1: income reported in PIT-8C (not issued by foreign brokers)
    int Pit8cPrzychody,
    int Pit8cKoszty,
    // Section C, row "Inne przychody"
    int InnePrzychody,
    int InneKoszty,
    // Section C, row "Razem"
    int RazemPrzychody,
    int RazemKoszty,
    int Dochod,
    int Strata,
    // Section D
    int Straty,
    int Podstawa,
    int Podatek,
    int PodatekNalezny,
    // Section G: flat tax under art. 29, 30, 30a not withheld by a payer (domestic; left empty by Pitly)
    int ZryczaltowanyNiepobrany,
    // Section G: flat tax on foreign income under art. 30a ust. 1 pkt 1-5, foreign tax paid, the difference
    int Zryczaltowany,
    int Zaplacony,
    int Roznica,
    int DoZaplaty)
{
    private static Pit38FormLayout Until2024(int version) => new(
        version,
        Pit8cPrzychody: 20, Pit8cKoszty: 21,
        InnePrzychody: 22, InneKoszty: 23,
        RazemPrzychody: 24, RazemKoszty: 25, Dochod: 26, Strata: 27,
        Straty: 28, Podstawa: 29, Podatek: 31, PodatekNalezny: 33,
        ZryczaltowanyNiepobrany: 44, Zryczaltowany: 45, Zaplacony: 46, Roznica: 47, DoZaplaty: 49);

    public static readonly Pit38FormLayout Version15 = Until2024(15);
    public static readonly Pit38FormLayout Version16 = Until2024(16);
    public static readonly Pit38FormLayout Version17 = Until2024(17);

    public static readonly Pit38FormLayout Version18 = new(
        18,
        Pit8cPrzychody: 20, Pit8cKoszty: 21,
        InnePrzychody: 22, InneKoszty: 23,
        RazemPrzychody: 26, RazemKoszty: 27, Dochod: 28, Strata: 29,
        Straty: 30, Podstawa: 31, Podatek: 33, PodatekNalezny: 35,
        ZryczaltowanyNiepobrany: 46, Zryczaltowany: 47, Zaplacony: 48, Roznica: 49, DoZaplaty: 51);

    /// <summary>Tax years whose form version was verified against the official form.</summary>
    private static readonly IReadOnlyDictionary<int, Pit38FormLayout> ByTaxYear = new Dictionary<int, Pit38FormLayout>
    {
        [2021] = Version15,
        [2022] = Version16,
        [2023] = Version16,
        [2024] = Version17,
        [2025] = Version18
    };

    public static IReadOnlyCollection<int> SupportedYears => ByTaxYear.Keys.OrderBy(y => y).ToList();

    /// <summary>The verified layout for the tax year, or null when no form version was verified for it.</summary>
    public static Pit38FormLayout? ForYear(int year) => ByTaxYear.GetValueOrDefault(year);
}
