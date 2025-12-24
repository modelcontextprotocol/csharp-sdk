using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests;

internal static class TextMaterializationTestHelpers
{
    /// <summary>
    /// Gets the JsonSerializerOptions for tests, depending on whether UTF-8 text content blocks are materialized.
    /// </summary>
    internal static JsonSerializerOptions GetOptions(bool materializeUtf8TextContentBlocks) =>
        materializeUtf8TextContentBlocks
            ? McpJsonUtilities.CreateOptions(materializeUtf8TextContentBlocks: true)
            : McpJsonUtilities.DefaultOptions;

    /// <summary>
    /// Gets the text from a ContentBlock, depending on whether UTF-8 text content blocks are materialized.
    /// </summary>
    internal static string GetText(ContentBlock contentBlock, bool materializeUtf8TextContentBlocks) =>
        materializeUtf8TextContentBlocks
            ? Assert.IsType<Utf8TextContentBlock>(contentBlock).Text
            : Assert.IsType<TextContentBlock>(contentBlock).Text;
}
