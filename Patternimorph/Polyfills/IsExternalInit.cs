#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Enables C# 9 <c>init</c> accessors on targets whose BCL lacks this type (e.g. netstandard2.0).
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
#endif
