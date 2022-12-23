/////////////////////////////////////////////////////////////////////////////////////
//
// ILCompose - Compose partially implementation both .NET language and IL assembler.
// Copyright (c) Kouji Matsui (@kozy_kekyo, @kekyo@mastodon.cloud)
//
// Licensed under Apache-v2: https://opensource.org/licenses/Apache-2.0
//
/////////////////////////////////////////////////////////////////////////////////////

.class public ILCompose.Sample.TestClass
    extends [mscorlib]System.Object
{
    .method public static int32 foobar(int32 a, int32 b) cil managed
    {
        .maxstack 2
        ldarg.0
        ldarg.1
        add
        ret
    }
}
