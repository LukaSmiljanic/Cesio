using System.Text;
using Modules.Documents;

namespace Platform.Integration.Tests;

/// <summary>Type detection by magic bytes and file name sanitization (task 006, security.md §4).</summary>
public sealed class DocumentFormatTests
{
    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, null)]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, null)]
    [InlineData(new byte[] { }, null)]
    public void Binary_types_are_detected_by_magic_bytes(byte[] head, string? expected) =>
        Assert.Equal(expected, DocumentService.DetectContentType(head));

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><Invoice/>", "application/xml")]
    [InlineData("\uFEFF<?xml version=\"1.0\"?><Invoice/>", "application/xml")]
    [InlineData("  \r\n<Invoice xmlns=\"urn:oasis\"/>", "application/xml")]
    [InlineData("<!DOCTYPE html><html/>", null)]
    [InlineData("plain text", null)]
    public void Xml_is_detected_with_or_without_a_declaration(string text, string? expected) =>
        Assert.Equal(expected, DocumentService.DetectContentType(Encoding.UTF8.GetBytes(text)));

    [Theory]
    [InlineData("../../Ugovor o faktoringu.pdf", "application/pdf", "Ugovor_o_faktoringu.pdf")]
    [InlineData("C:\\temp\\scan.exe", "application/pdf", "scan.pdf")]
    [InlineData("<script>.png", "image/png", "_script_.png")]
    [InlineData("", "application/xml", "document.xml")]
    [InlineData(null, "image/jpeg", "document.jpg")]
    public void File_names_keep_only_safe_characters_and_the_detected_extension(string? name, string contentType, string expected) =>
        Assert.Equal(expected, DocumentService.SanitizeFileName(name, contentType));

    [Fact]
    public void Long_file_names_are_cut_to_120_characters()
    {
        var name = DocumentService.SanitizeFileName(new string('a', 500) + ".pdf", "application/pdf");

        Assert.Equal(120, name.Length);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
    }
}
