using System;
using System.Runtime.CompilerServices;

// The plugin calls members that are non-public in the shipped game assemblies. Mono does not enforce the
// access check for those calls today, but declaring the exemption keeps the plugin working if it ever does.
[assembly: IgnoresAccessChecksTo("Assembly-CSharp")]
[assembly: IgnoresAccessChecksTo("Assembly-CSharp-firstpass")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
            AssemblyName = assemblyName;
        }

        public string AssemblyName { get; }
    }
}
