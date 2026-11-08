// Unity's .NET profile does not ship System.Runtime.CompilerServices.IsExternalInit, which the
// compiler requires to emit the `init` accessors that records are built from. Declaring it here
// (internal, so it stays inside Pantheon.Core) is the standard shim. Delete if Unity ever ships it.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
