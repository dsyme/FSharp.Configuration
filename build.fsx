// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------
#I  @"packages/FAKE/tools/"
#r @"packages/FAKE/tools/FakeLib.dll"
open Fake 
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System

let projects = [|"FSharp.Configuration"|]


// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "fsprojects"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "FSharp.Configuration"

let summary = "Type providers for the configuration of .NET projects."
let description = "The FSharp.Configuration project contains type providers for the configuration of .NET projects."
let authors = ["Steffen Forkmann"; "Sergey Tihon"; "Daniel Mohl"; "Tomas Petricek"; "Ryan Riley"; "Mauricio Scheffer"; "Phil Trelford"; "Vasily Kirichenko"; "Reed Copsey, Jr."]
let tags = "F# fsharp typeproviders Management PowerShell"

let solutionFile  = "FSharp.Configuration"

let testAssemblies = "tests/**/bin/Release/*.Tests*.dll"
let cloneUrl = "git@github.com:fsprojects/FSharp.Configuration.git"
let nugetDir = "./nuget/"

// Read additional information from the release notes document
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = parseReleaseNotes (IO.File.ReadAllLines "RELEASE_NOTES.md")

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
  for project in projects do
    let fileName = "src/" + project + "/AssemblyInfo.fs"
    CreateFSharpAssemblyInfo fileName
        [ Attribute.Title project
          Attribute.Product project
          Attribute.Description summary
          Attribute.Version release.AssemblyVersion
          Attribute.FileVersion release.AssemblyVersion ] 
)

// --------------------------------------------------------------------------------------
// Clean build results & restore NuGet packages

Target "RestorePackages" RestorePackages

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"; nugetDir]
)

Target "CleanDocs" (fun _ ->
    CleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    !! (solutionFile + ".sln")
    |> MSBuildRelease "" "Rebuild"
    |> ignore

    !! (solutionFile + ".Tests.sln")
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner & kill test runner when complete

Target "RunTests" (fun _ ->
    ActivateFinalTarget "CloseTestRunner"

    !! testAssemblies
    |> NUnit (fun p ->
        { p with
            DisableShadowCopy = true
            TimeOut = TimeSpan.FromMinutes 20.
            OutputFile = "TestResults.xml" })
)

FinalTarget "CloseTestRunner" (fun _ ->  
    ProcessHelper.killProcess "nunit-agent.exe"
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun _ ->
    // Format the description to fit on a single line (remove \r\n and double-spaces)
    let description = description.Replace("\r", "")
                                 .Replace("\n", "")
                                 .Replace("  ", " ")
    let project = projects.[0]

    let nugetDocsDir = nugetDir @@ "docs"
    let nugetlibDir = nugetDir @@ "lib/net40"

    CleanDir nugetDocsDir
    CleanDir nugetlibDir
        
    CopyDir nugetlibDir "bin" (fun file -> file.Contains "FSharp.Core." |> not)
    CopyDir nugetDocsDir "./docs/output" allFiles
    
    NuGet (fun p -> 
        { p with   
            Authors = authors
            Project = project
            Summary = summary
            Description = description
            Version = release.NugetVersion
            ReleaseNotes = release.Notes |> toLines
            Tags = tags
            OutputPath = nugetDir
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey"
            Dependencies = [] })
        (project + ".nuspec")
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target "GenerateDocs" (fun _ ->
    executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"] [] |> ignore
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" (fun _ ->
    let tempDocsDir = "temp/gh-pages"
    CleanDir tempDocsDir
    Repository.cloneSingleBranch "" (gitHome + "/" + gitName + ".git") "gh-pages" tempDocsDir

    fullclean tempDocsDir
    CopyRecursive "docs/output" tempDocsDir true |> tracefn "%A"
    StageAll tempDocsDir
    Git.Commit.Commit tempDocsDir (sprintf "Update generated documentation for version %s" release.NugetVersion)
    Branches.push tempDocsDir
)

Target "Release" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  ==> "RestorePackages"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "RunTests"
  ==> "All"

"All" 
  ==> "CleanDocs"
  ==> "GenerateDocs"
  ==> "ReleaseDocs"
  ==> "NuGet"
  ==> "Release"

RunTargetOrDefault "All"
