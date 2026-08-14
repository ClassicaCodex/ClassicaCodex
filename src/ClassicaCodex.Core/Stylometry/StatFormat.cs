namespace ClassicaCodex.Core.Stylometry;

/// <summary>
/// Formatting for the signed statistics the validation bench reports.
///
/// Exists because "+0.00;-0.00" does not do what it looks like it does. .NET's
/// rule for a two-section custom numeric format is that the second section
/// applies to negative values - EXCEPT that a negative value which rounds to
/// zero under that section is formatted using the FIRST section instead. So a
/// correlation band whose lower end is -0.0053, which is what rho +0.45 over
/// nineteen works produces, comes out through the positive branch.
///
/// The result on screen was "-+0.00 to +0.75", which is not a number.
///
/// The deeper problem is that a sign is being asserted at a magnitude where it
/// carries no information. A band running from -0.0053 is, for every purpose
/// this bench has, a band running from zero; printing it as negative invites
/// the reading that the correlation might run the other way, and printing it as
/// positive invites the reading that it definitely does not. Below the
/// displayed precision the honest answer is neither, so it prints "0.00" with
/// no sign at all.
/// </summary>
public static class StatFormat
{
    /// <summary>
    /// A signed two-decimal value, without a sign when the magnitude is too
    /// small for one to mean anything.
    /// </summary>
    public static string Signed(double value) =>
        Math.Abs(value) < 0.005
            ? "0.00"
            : (value < 0 ? "-" : "+") + Math.Abs(value).ToString("0.00");

    /// <summary>A three-decimal version, for margins and separations.</summary>
    public static string Signed3(double value) =>
        Math.Abs(value) < 0.0005
            ? "0.000"
            : (value < 0 ? "-" : "+") + Math.Abs(value).ToString("0.000");

    /// <summary>A correlation interval, as it appears in the grid.</summary>
    public static string Band((double Low, double High) band) =>
        $"{Signed(band.Low)} to {Signed(band.High)}";
}
