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
  
module Kernel =
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



module Session = 
    open Laika.Internal.JupyterApi
    [<Fact>]
    let ``empty session list``() = 
        withJupyter
            (fun s -> 
                s
                |> Jupyter.sessionList 
                |> Assert.Empty)
            
    [<Fact>]
    let ``start session``() = 
        withKernel Kernel.kernelName
            (fun s k -> 
                let session = 
                    {
                        Id = ""
                        Path = "Untitled.ipynb"
                        Name = ""
                        Type = "notebook"
                        Kernel = k
                    }
                
                let s2 = Jupyter.session session s
                Assert.True((k.Id = s2.Kernel.Id)))
            
    [<Fact>]
    let ``non-empty session list``() = 
        withKernel Kernel.kernelName
            (fun s k -> 
                let session = 
                    {
                        Id = ""
                        Path = "Untitled.ipynb"
                        Name = ""
                        Type = "notebook"
                        Kernel = k
                    }
                
                let s2 = Jupyter.session session s
                let l = Jupyter.sessionList s
                Assert.True((l.Length = 1))
                Assert.True((l.[0] = s2)))
            
            
            
    [<Fact>]
    let ``existing session``() = 
        withKernel Kernel.kernelName
            (fun s k -> 
                let session = 
                    {
                        Id = ""
                        Path = "Untitled.ipynb"
                        Name = "MySession"
                        Type = "notebook"
                        Kernel = k
                    }
                let s2 = Jupyter.session session s
                let s3 = Jupyter.session session s 
                Assert.True((s2.Id = s3.Id)))
            
    [<Fact>]
    let ``session from id``() = 
        withKernel Kernel.kernelName
            (fun s k -> 
                let session = 
                    {
                        Id = ""
                        Path = "Untitled.ipynb"
                        Name = "MySession"
                        Type = "notebook"
                        Kernel = k
                    }
                let s2 = Jupyter.session session s
                let s3 = Jupyter.sessionGet s2.Id s 
                Assert.True((s2.Name = s3.Name)))
            
    [<Fact>]
    let ``rename session``() = 
        withKernel Kernel.kernelName
            (fun s k -> 
                let session = 
                    {
                        Id = ""
                        Path = "Untitled.ipynb"
                        Name = "MySession"
                        Type = "notebook"
                        Kernel = k
                    }
                let s2 = Jupyter.session session s
                let s3 = Jupyter.sessionRename {s2 with Name = "newName"} s2.Id s
                Assert.Equal("newName", s3.Name)
                let l = Jupyter.sessionList s 
                Assert.Equal("newName", l.[0].Name))
            
    [<Fact>]
    let ``delete session``() = 
        withKernel Kernel.kernelName
            (fun s k -> 
                let session = 
                    {
                        Id = ""
                        Path = "Untitled.ipynb"
                        Name = "MySession"
                        Type = "notebook"
                        Kernel = k
                    }
                let s2 = Jupyter.session session s
                let l = Jupyter.sessionList s 
                Assert.Equal("MySession", l.[0].Name)
                let resp = Jupyter.sessionDelete s2.Id s
                Assert.Equal(enum 204, resp.StatusCode)
                let l2 = Jupyter.sessionList s 
                Assert.Empty(l2))
            