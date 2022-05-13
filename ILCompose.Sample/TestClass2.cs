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
    public class TestClass2
    {
        [Description("This is CIL code")]
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern int TestInCIL(int a, int b);
    }
}
