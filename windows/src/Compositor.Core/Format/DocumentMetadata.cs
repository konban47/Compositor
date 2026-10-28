using System.Text.Json.Serialization;
using System.Xml;

namespace Compositor.Core.Format;

/// <summary>Editable, portable XMP fields. Unknown/private camera tags are not retained.</summary>
public sealed record DocumentMetadata
{
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string Copyright { get; init; } = "";
    public string Email { get; init; } = "";
    public string Website { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string CameraMake { get; init; } = "";
    public string CameraModel { get; init; } = "";
    public string DateTaken { get; init; } = "";

    [JsonIgnore] public bool IsEmpty => this == new DocumentMetadata();
    [JsonIgnore] public bool IsValid
    {
        get
        {
            var fields = new[] { Title, Author, Description, Copyright, Email, Website, Keywords, CameraMake, CameraModel, DateTaken };
            if (fields.Any(s => s is null || s.Length > 32768) || fields.Sum(s => s.Length) > 131072) return false;
            try { foreach (var value in fields) XmlConvert.VerifyXmlChars(value); return true; }
            catch (XmlException) { return false; }
        }
    }
}
