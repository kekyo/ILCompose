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

namespace ILCompose.Sample
{
    [TestFixture]
    public class TestClass
    {
        [TestCase(1, 2, ExpectedResult = 3)]
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern int TestInCIL(int a, int b);

        [TestCase(ExpectedResult = 123)]
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern int TestInCIL();

        [TestCase(123, ExpectedResult = "123")]
        [MethodImpl(MethodImplOptions.ForwardRef)]
        public static extern string TestInCIL(int a);
    }
}
