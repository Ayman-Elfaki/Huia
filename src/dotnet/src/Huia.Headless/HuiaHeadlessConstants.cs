namespace Huia.Headless;

/// <summary>Constants for the Huia Headless authentication flavor.</summary>
public static class HuiaHeadlessConstants
{
    /// <summary>Authentication schemes used by Huia Headless.</summary>
    public static class Schemes
    {
        /// <summary>The primary JWT bearer scheme.</summary>
        public const string Bearer = "Huia:Headless:Bearer";
    }

    /// <summary>Token types issued by Huia Headless.</summary>
    public static class TokenTypes
    {
        /// <summary>Standard bearer token.</summary>
        public const string Bearer = "Bearer";
    }
}
