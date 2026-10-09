using SarawakBizOps.Api.Services.Ai;

namespace SarawakBizOps.Api.Tests;

/// <summary>AI-2, no database: the deterministic safety backstop (docs/ai/decisions.md G13, G19, G20).</summary>
public class SafetyTermsTests
{
    public static TheoryData<string> AllTerms()
    {
        var data = new TheoryData<string>();
        foreach (var term in SafetyTerms.English.Concat(SafetyTerms.Malay).Concat(SafetyTerms.Loanwords))
        {
            data.Add(term);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTerms))]
    public void Every_term_matches_inside_a_sentence(string term)
    {
        Assert.True(SafetyTerms.Matches($"Sila semak ({term.ToUpperInvariant()}) sebelum mula."), term);
    }

    [Theory]
    [InlineData("De-energise the compressor first.", "de energise")]
    [InlineData("de energise the compressor first", "de energise")]
    [InlineData("Apply LOCK-OUT at the panel.", "lock out")]
    [InlineData("Tutup suis utama dahulu.", "suis utama")]
    [InlineData("Periksa bahagian berputar.", "berputar")]
    [InlineData("Kipas berputar laju, jangan sentuh.", "berputar")]
    [InlineData("Matikan MCB di DB.", "mcb")]
    [InlineData("Asingkan bekalan sebelum buka panel.", "asingkan bekalan")]
    [InlineData("Pastikan kapasitor telah dinyahcas — guna nyahcas probe.", "kapasitor")]
    public void Phrases_match_whatever_the_case_or_punctuation(string text, string expected)
    {
        Assert.Equal(expected, SafetyTerms.FirstMatch(text));
    }

    [Theory]
    [InlineData("Measure 415V across L1 and L2.", "415V")]
    [InlineData("Supply is 11 kV at the transformer.", "11 kV")]
    [InlineData("Expect 240 VAC at the terminal.", "240 VAC")]
    [InlineData("Charge to 150 psi.", "150 psi")]
    [InlineData("Set the regulator to 2.5 bar.", "2.5 bar")]
    [InlineData("Head should read 600 kPa.", "600 kPa")]
    [InlineData("Unit runs on R410A.", "R410A")]
    [InlineData("Top up with r-22.", "r-22")]
    [InlineData("Unit uses R134a.", "R134a")]
    public void Value_patterns_match_voltages_pressures_and_refrigerant_codes(string text, string expected)
    {
        Assert.Equal(expected, SafetyTerms.FirstMatch(text));
    }

    [Theory]
    [InlineData("Asingkan skru dan simpan dalam bekas.")] // put the screws aside
    [InlineData("Guna kunci 10mm untuk buka penutup.")] // a spanner
    [InlineData("Pastikan arus udara tidak tersekat.")] // airflow
    [InlineData("Tekan butang reset sekali.")] // "tekan" is not "tekanan"
    [InlineData("Check the filter is clean and record the reading.")]
    [InlineData("Order part NC-450 bearing kit; see KB-10T manual.")]
    [InlineData("Replace the lamp and the clamp.")] // "amp" only as a whole word
    [InlineData("Delivery was on time.")] // "live" only as a whole word
    [InlineData("Tighten to 25 Nm.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ordinary_repair_talk_does_not_match(string? text)
    {
        Assert.Null(SafetyTerms.FirstMatch(text));
        Assert.False(SafetyTerms.Matches(text));
    }
}
