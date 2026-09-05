namespace ClassicaCodex.UI;

internal static class BronzeIcons
{
    public static Icon Bestiary() => Load("BronzeBestiary");
    public static Icon DivineGift() => Load("BronzeDivineGift");
    public static Icon Laurels() => Load("BronzeLaurels");
    public static Bitmap ButtonImage(string name)
    {
        using var icon = Load("Bronze" + name);
        using var small = new Icon(icon, 20, 20);
        return small.ToBitmap();
    }

    private static Icon Load(string name)
    {
        using var stream = typeof(BronzeIcons).Assembly.GetManifestResourceStream($"ClassicaCodex.UI.Icons.{name}.ico")
            ?? throw new InvalidOperationException($"The {name} icon resource is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
