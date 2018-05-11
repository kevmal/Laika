namespace Laika.Tests

open Xunit
module Crap = 
    [<Fact>]
    let anothertest() = Assert.True(true)
    

type SomeTests() =
    
    [<Fact>]
    member x.test() = Assert.True(true)