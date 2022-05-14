/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

.class public ILCompose.UnitTestSample.TestClass
    extends [System.Runtime]System.Object
{
    .method public static int32 TestInCIL(int32 a, int32 b) cil managed
    {
        .maxstack 2
        ldarg.0
        ldarg.1
        add
        ret
    }
    .method public static int32 TestInCIL() cil managed
    {
        .maxstack 2
        ldc.i4 123
        ret
    }
    .method public static class [System.Runtime]System.String TestInCIL(int32 a) cil managed
    {
        .maxstack 1
        ldarga.s 0
        call instance class [System.Runtime]System.String [System.Runtime]System.Int32::ToString()
        ret
    }
}
