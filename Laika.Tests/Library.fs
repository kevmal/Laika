namespace Laika.Tests

open Laika
open Jupyter
open System.Diagnostics
open System
open System.Web
open Xunit

[<AutoOpen>]
module Helper = 
    open System.Threading

    let jupyterLocation = @"E:\install\Anaconda3\Scripts\jupyter.exe"
    let jupyterUrl = "http://localhost:8888"

    let startJupyter2() =
            let path = Environment.GetEnvironmentVariable "PATH"
            let jdir = IO.Path.GetDirectoryName(jupyterLocation)
            Environment.SetEnvironmentVariable("PATH", jdir + string IO.Path.PathSeparator + path)
            let pinfo = ProcessStartInfo()
            pinfo.FileName <- jupyterLocation
            pinfo.Arguments <- "notebook --no-browser"
            pinfo.UseShellExecute <- false
            pinfo.RedirectStandardOutput <- true
            pinfo.RedirectStandardInput <- true
            pinfo.RedirectStandardError <- true
            let p = Process.Start(pinfo)
            let u = 
                Seq.initInfinite (fun _ -> p.StandardError.ReadLine())
                |> Seq.pick
                    (fun x ->
                        if x.Contains("The Jupyter Notebook is running at:") then
                            (x.Split ' ') |> Array.last |> Some
                        else  
                            None
                    )
                |> Uri
            let q = HttpUtility.ParseQueryString(u.Query)
            let token = 
                if q.AllKeys |> Seq.contains "token" then
                    Some q.["token"]
                else    
                    None
                
            p, {
                Url = Uri(u.AbsoluteUri.Replace(u.Query, ""))
                Token = token
                Username = None
                Password = None
            }


    let kill2 (p : Process) = //This is unfortunate
        let pinfo = ProcessStartInfo("taskkill")
        pinfo.Arguments <- sprintf "/PID %d /F /T " p.Id
        pinfo.UseShellExecute <- false
        Process.Start(pinfo)
    
    let startJupyter() =
        (),{
            Url = Uri(jupyterUrl)
            Token = Some "8d382eb1f14ac525235d5ea1e504d3b8f427910e90a20bd8"
            Username = None
            Password = None
        }

    let kill () = ()

    let withJupyter f = 
        let p,s = startJupyter() 
        f s
        kill p
    let withKernel name f = 
        let p,s = startJupyter() 
        let k = Jupyter.kernel name s
        f s k
        kill p
    let first (o : IObservable<'a>) = 
        let mutable v = Unchecked.defaultof<'a>
        use e = new ManualResetEventSlim()
        use d = 
            o.Subscribe
                (fun x -> 
                    if not e.IsSet then 
                        v <- x
                        e.Set())
        if e.Wait(TimeSpan.FromSeconds 5.0) then
            Some v
        else
            None
  
    //let checkNulls (o : 't) =
    //    typeof<'t>.GetProperties()
    //    |> Seq.iter
    //        (fun x ->
    //            let t = x.PropertyType
    //            if not(t.ContainsGenericParameters && t.GetGenericTypeDefinition() = typedefof<_ option>) then
    //                Assert.True(x.GetMethod.Invoke(o, Array.empty) |> isNull |> not, sprintf "%s %s should not be null" x.DeclaringType.FullName x.Name)
    //            if t.
    //        )

module Kernel =
    open Laika.Internal.JupyterApi

    let kernelName = "python3"
    [<Fact>]
    let ``start a kernel``() = 
        withJupyter
            (fun server ->
                let k = Jupyter.kernel kernelName server//{server with Url = Uri("http://localhost:9394")}
                Assert.Equal(kernelName, k.Name)
            )
    [<Fact(Skip="Needs ability to start/stop jupyter")>]
    let ``list kernels empty``() = 
        withJupyter
            (fun server ->
                let kss = Jupyter.kernelList server
                Assert.Empty kss
            )
    [<Fact>]
    let ``list kernels non-empty``() = 
        withJupyter
            (fun server ->
                let k = Jupyter.kernel kernelName server
                Assert.Equal(kernelName, k.Name)
                let kernels = Jupyter.kernelList server
                Assert.NotEmpty(kernels)
                Assert.True(kernels |> Seq.exists (fun i -> i.Name = kernelName), "Kernel started but not in kernel list")
            )
    [<Fact>]
    let ``delete kernel``() = 
        withKernel kernelName
            (fun server k ->
                let resp = server |> Jupyter.kernelDelete k.Id
                Assert.Equal(enum 204, resp.StatusCode)
            )

    [<Fact>]
    let ``execute 1 + 1``() = 
        withKernel kernelName
            (fun server k ->
                let client = Jupyter.kernelClient k.Id server 
                let result =
                    Jupyter.executeRequest "1 + 1"
                    |> Jupyter.sendMessageObservable client
                    |> Observable.choose (fun x -> match x.Content with :? Jupyter.ExecuteResult as c -> Some c | _ -> None)
                    |> first
                Assert.True(result.IsSome, "No result before timeout")
                Assert.Equal(result.Value.Data.["text/plain"], "2")

            )

    [<Fact>]
    let ``kernel specs``() = 
        withJupyter 
            (fun server ->
                let specs = Jupyter.kernelSpecs server
                Assert.NotEmpty(specs.Kernelspecs)
                Assert.True(specs.Kernelspecs.[specs.Default].Name = specs.Default)
            )
    [<Fact>]
    let ``kernel check for null fields``() = 
        //TODO generalize null checking
        withJupyter 
            (fun server ->
                let specs = Jupyter.kernelSpecs server
                typeof<KernelspecsResponse>.GetProperties()
                |> Seq.iter
                    (fun x ->
                        let t = x.PropertyType
                        if not(t.ContainsGenericParameters && t.GetGenericTypeDefinition() = typedefof<_ option>) then
                            Assert.True(x.GetMethod.Invoke(specs, Array.empty) |> isNull |> not, sprintf "%s %s should not be null" x.DeclaringType.FullName x.Name)
                    )
                let checkSpec (spec : KernelSpec) = 
                    typeof<KernelSpec>.GetProperties()
                    |> Seq.iter
                        (fun x ->
                            let t = x.PropertyType
                            if not(t.ContainsGenericParameters && t.GetGenericTypeDefinition() = typedefof<_ option>) then
                                Assert.True(x.GetMethod.Invoke(spec, Array.empty) |> isNull |> not, sprintf "%s %s should not be null" x.DeclaringType.FullName x.Name)
                        )
                    typeof<KernelSpecFile>.GetProperties()
                    |> Seq.iter
                        (fun x ->
                            let t = x.PropertyType
                            if not(t.ContainsGenericParameters && t.GetGenericTypeDefinition() = typedefof<_ option>) then
                                Assert.True(x.GetMethod.Invoke(spec.KernelSpecFile, Array.empty) |> isNull |> not, sprintf "%s %s should not be null" x.DeclaringType.FullName x.Name)
                        )
                specs.Kernelspecs.Values |> Seq.iter (checkSpec)
            )


module Content = 
    open Laika.Internal.JupyterApi
    [<Fact>]
    let ``simple directory listing``() = 
        withJupyter
            (fun s -> 
                let contents = 
                    s
                    |> Jupyter.contents "directory" "text" false ""
                Assert.Equal("directory", contents.Type))
            
    [<Fact(Skip="Needs ability to start/stop jupyter")>]
    let ``new untitled``() = 
        withJupyter
            (fun s -> 
                let contents = 
                    s
                    |> Jupyter.contentsEmptyNotebook ""
                Assert.True(contents.Name.StartsWith("Untitled"))
                Assert.True(System.IO.FileInfo(contents.Name).Length > 0L)
                IO.File.Delete(contents.Name)
            )
            

