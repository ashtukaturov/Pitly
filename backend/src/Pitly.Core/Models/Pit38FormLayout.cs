namespace Pitly.Core.Models;

/// <summary>
/// Field numbers (poz.) of the PIT-38 form version that applies to a tax year. The Ministry renumbered the form
/// for 2025 (PIT-38(18)); 2024 and earlier use PIT-38(17), where sections D and G sit two positions earlier.
/// Section C (poz. 20-27) is the same in both versions.
/// </summary>
public record Pit38FormLayout(
    int Version,
    int Straty,
    int Podstawa,
    int Podatek,
    int PodatekNalezny,
    int Zryczaltowany,
    int Zaplacony,
    int Roznica,
    int DoZaplaty)
{
    public static readonly Pit38FormLayout Version17 = new(17, 28, 29, 31, 33, 45, 46, 47, 49);
    public static readonly Pit38FormLayout Version18 = new(18, 30, 31, 33, 35, 47, 48, 49, 51);

    public static Pit38FormLayout ForYear(int year) => year >= 2025 ? Version18 : Version17;
}
