namespace Newtin.Server.Validation;

public static class WhiteboardLimits
{
    public const int MaxElements = 5000;
    public const int MaxCategories = 50;
    public const int MaxTextLength = 2000;
    public const int MaxPointsPerStroke = 5000;
    public const int MaxPointsPerProgress = 200;
    public const int MaxCategoryNameLength = 40;
    public const double MaxCoordinate = 100_000;
    public const double MinStrokeWidth = 1;
    public const double MaxStrokeWidth = 64;
    public const double DefaultStrokeWidth = 3;
    public const int MaxMessageBytes = 256 * 1024;
    public const int MaxOperationsPerSecond = 150;
    public const double MinFontSize = 8;
    public const double MaxFontSize = 120;
    public const double DefaultFontSize = 24;
    public const string DefaultTextColor = "#17131F";

    public const string DefaultNoteColor = "#F2B544";
    public const string DefaultStrokeColor = "#FFFFFF";
    public const string DefaultCategoryColor = "#A00082";
}