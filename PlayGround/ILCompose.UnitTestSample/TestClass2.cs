/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

using System.Runtime.CompilerServices;

using NUnit.Framework;

namespace ILCompose.UnitTestSample
{
    [TestFixture]
    public class TestClass2
    {
        [TestCase(1, 2, ExpectedResult = -1)]
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern int TestInCIL(int a, int b);
    }
}
