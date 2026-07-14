namespace FPTUniRAG.Services;

internal static class ChunkCharacterCounter
{
    public static int Count(string value)
    {
        return value.Count(IsCountedCharacter);
    }

    public static bool IsCountedCharacter(char character)
    {
        return character is not '\r' and not '\n';
    }
}
