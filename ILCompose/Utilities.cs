/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System.IO;
using System.Linq;

using Mono.Cecil;

namespace ILCompose
{
    internal static class Utilities
    {
        public static string GetDirectoryPath(string path)
        {
            var d = Path.GetDirectoryName(path);
            if (d == null)
            {
                return Path.DirectorySeparatorChar.ToString();
            }
            else if (string.IsNullOrWhiteSpace(d))
            {
                return Path.GetFullPath(".");
            }
            else
            {
                return Path.GetFullPath(d);
            }
        }

        public static bool IsForwardRef(MethodDefinition method) =>
            // Native CLR flag,
            (method.ImplAttributes & MethodImplAttributes.ForwardRef) == MethodImplAttributes.ForwardRef ||
            // or custom attribute applied last application.
            // HACK: Complicated problem: because if only the CIL source code is changed in the next build,
            //   the forwardref flag is missing from the primary assembly,
            //   so it cannot correctly determine which method to replace,
            //   and the compose fails but only the build runs.
            //   At the last build, instead of losing the forwardref flag,
            //   the original attribute is manually applied so that it can be distinguished again;
            //   the C# compiler implicitly replaces this attribute with the forwardref flag.
            //   Thus, unless intentionally inserted into the assembly,
            //   this flag is not likely to get in the way,
            //   and there is no need to define an additional attribute type.
            method.CustomAttributes.Any(ca =>
                ca.AttributeType.FullName == "System.Runtime.CompilerServices.MethodImplAttribute" &&
                ca.HasConstructorArguments &&
                ca.ConstructorArguments.Any(a =>
                    (a.Value is short s && (s & (short)MethodImplAttributes.ForwardRef) != 0) ||
                    (a.Value is int i && (i & (int)MethodImplAttributes.ForwardRef) != 0)));

    }
}
