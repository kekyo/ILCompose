/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ILCompose.Sample
{
    public class TestClass
    {
        [DefaultValue("It isn't removed!")]
#if NETSTANDARD1_6
        [MethodImpl((MethodImplOptions)16)]
#else
        [MethodImpl(MethodImplOptions.ForwardRef)]
#endif
        public static extern int TestInCIL(int a, int b);

        [EditorBrowsable(EditorBrowsableState.Advanced)]
#if NETSTANDARD1_6
        [MethodImpl((MethodImplOptions)16)]
#else
        [MethodImpl(MethodImplOptions.ForwardRef)]
#endif
        public static extern string TestInCIL(int a);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public static string TestInCSharp(int a) =>
            a.ToString();
    }
}
