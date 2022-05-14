/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

.class public ILCompose.Sample.TestClass
    extends System.Object
{
    .method public static int32 TestInCIL(int32 a, int32 b) cil managed
    {
        .maxstack 2
        ldarg.0
        ldarg.1
        add
        ret
    }
    .method public static class System.String TestInCIL(int32 a) cil managed
    {
        // [Description("ABC")]
        .custom instance void [netstandard]System.ComponentModel.DescriptionAttribute::.ctor(string) = (
            01 00 03 41 42 43 00 00
        )
        .maxstack 1
        ldarga.s 0
        call instance class System.String [netstandard]System.Int32::ToString()
        ret
    }
}
