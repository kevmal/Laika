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

    let jupyterLocation = @"C:\Users\User1\Anaconda2\envs\tensorflow\Scripts\jupyter.exe"
    let jupyterUrl = ""


    let startJupyter() =
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
    let withJupyter f = 
        let p,s = startJupyter() 
        f s
        p.Kill()
    let withKernel name f = 
        let p,s = startJupyter() 
        let k = Jupyter.kernel name s
        f s k
        p.Kill()
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
  
module Basic =
    let kernelName = "python3"
    [<Fact>]
    let ``start a kernel``() = 
        withJupyter
            (fun server ->
                let k = Jupyter.kernel kernelName server//{server with Url = Uri("http://localhost:9394")}
                Assert.Equal(kernelName, k.Name)
            )
    [<Fact>]
    let ``list kernels empty``() = 
        withJupyter
            (fun server ->
                let kss = Jupyter.listKernels server
                Assert.Empty kss
            )
    [<Fact>]
    let ``list kernels non-empty``() = 
        withJupyter
            (fun server ->
                let k = Jupyter.kernel kernelName server
                Assert.Equal(kernelName, k.Name)
                let kernels = Jupyter.listKernels server
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



