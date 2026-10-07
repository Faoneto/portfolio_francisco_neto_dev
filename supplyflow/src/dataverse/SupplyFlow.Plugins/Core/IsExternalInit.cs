#if NETFRAMEWORK
// Polyfill that enables C# 'init' accessors and records when targeting .NET Framework 4.6.2.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
