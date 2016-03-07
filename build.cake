#tool nuget:?package=XamarinComponent

#addin "Cake.Xamarin"
#addin "Cake.XCode"
#addin "Cake.FileHelpers"

using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

var TARGET = Argument ("t", Argument ("target", Argument ("Target", "Default")));

const string okio_version                 = "1.6.0"; // OkIO
const string okhttp_version               = "2.7.5"; // OkHttp
const string okhttp3_version              = "3.2.0"; // OkHttp3
const string okhttpws_version             = "2.7.5"; // OkHttp-WS
const string okhttp3ws_version            = "3.2.0"; // OkHttp3-WS
const string okhttpurlconnection_version  = "2.7.5"; // OkHttp-UrlConnection
const string picasso_version              = "2.5.2"; // Picasso
const string androidtimessquare_version   = "1.6.5"; // AndroidTimesSquare
const string socketrocket_version         = "0.4.2"; // SocketRocket
const string valet_version                = "2.2.1"; // Valet
const string aardvark_version             = "1.4.0"; // Aardvark
const string seismic_version              = "1.0.2"; // Seismic
const string pollexor_version             = "2.0.4"; // Pollexor
const string retrofit_version             = "1.9.0"; // Retrofit

////////////////////////////////////////////////////////////////////////////////////////////////////
// TOOLS & FUNCTIONS - the bits to make it all work
////////////////////////////////////////////////////////////////////////////////////////////////////

var NugetToolPath = File ("./tools/nuget.exe");
var XamarinComponentToolPath = File ("./tools/XamarinComponent/tools/xamarin-component.exe");
var GitToolPath = EnvironmentVariable ("GIT_EXE") ?? (IsRunningOnWindows () ? "C:\\Program Files (x86)\\Git\\bin\\git.exe" : "git");

Information (MakeAbsolute (File (".")).ToString ());
Information (MakeAbsolute (File ("./tools/nuget.exe")).ToString ());
Information (FileExists (MakeAbsolute (File ("./tools/nuget.exe"))).ToString ());

var RunGit = new Action<DirectoryPath, string> ((directory, arguments) =>
{
	StartProcess (GitToolPath, new ProcessSettings {
        Arguments = arguments,
        WorkingDirectory = directory,
    });
});

var RunNuGetRestore = new Action<FilePath> ((solution) =>
{
    NuGetRestore (solution, new NuGetRestoreSettings { 
        ToolPath = NugetToolPath
    });
});
var RunComponentRestore = new Action<FilePath> ((solution) =>
{
    RestoreComponents (solution, new XamarinComponentRestoreSettings { 
		ToolPath = XamarinComponentToolPath
    });
});
 
var PackageNuGet = new Action<FilePath, DirectoryPath> ((nuspecPath, outputPath) =>
{
	// NuGet messes up path on mac, so let's add ./ in front twice
	var basePath = IsRunningOnUnix () ? "././" : "./";

	if (!DirectoryExists (outputPath)) {
		CreateDirectory (outputPath);
	}

    NuGetPack (nuspecPath, new NuGetPackSettings { 
        Verbosity = NuGetVerbosity.Detailed,
        OutputDirectory = outputPath,		
        BasePath = basePath,
        ToolPath = NugetToolPath
    });				
});

var RunLipo = new Action<DirectoryPath, FilePath, FilePath[]> ((directory, output, inputs) =>
{
    if (!IsRunningOnUnix ()) {
        throw new InvalidOperationException ("lipo is only available on Unix.");
    }
    
    var dir = directory.CombineWithFilePath (output).GetDirectory ();
    if (!DirectoryExists (dir)) {
        CreateDirectory (dir);
    }

	var inputString = string.Join(" ", inputs.Select (i => string.Format ("\"{0}\"", i)));
	StartProcess ("lipo", new ProcessSettings {
		Arguments = string.Format("-create -output \"{0}\" {1}", output, inputString),
		WorkingDirectory = directory,
	});
});

var BuildXCode = new Action<FilePath, string, DirectoryPath, bool> ((project, libraryTitle, workingDirectory, isMac) =>
{
    if (!IsRunningOnUnix ()) {
        return;
    }
    
    var fatLibrary = string.Format("lib{0}.a", libraryTitle);

	var output = string.Format ("lib{0}.a", libraryTitle);
	var i386 = string.Format ("lib{0}-i386.a", libraryTitle);
	var x86_64 = string.Format ("lib{0}-x86_64.a", libraryTitle);
	var armv7 = string.Format ("lib{0}-armv7.a", libraryTitle);
	var armv7s = string.Format ("lib{0}-armv7s.a", libraryTitle);
	var arm64 = string.Format ("lib{0}-arm64.a", libraryTitle);
	
	var buildArch = new Action<string, string, FilePath> ((sdk, arch, dest) => {
		if (!FileExists (dest)) {
            XCodeBuild (new XCodeBuildSettings {
                Project = workingDirectory.CombineWithFilePath (project).ToString (),
                Target = libraryTitle,
                Sdk = sdk,
                Arch = arch,
                Configuration = "Release",
            });
            var outputPath = workingDirectory.Combine ("build").Combine (isMac ? "Release" : ("Release-" + sdk)).CombineWithFilePath (output);
            CopyFile (outputPath, dest);
        }
	});
	
    if (isMac) {
        // not supported anymore
        // buildArch ("macosx", "i386", workingDirectory.CombineWithFilePath (i386));
        buildArch ("macosx", "x86_64", workingDirectory.CombineWithFilePath (x86_64));
        
		if (!FileExists (workingDirectory.CombineWithFilePath (fatLibrary))) {
            RunLipo (workingDirectory, fatLibrary, new [] {
                (FilePath)x86_64 
            });
        }
    } else {
        buildArch ("iphonesimulator", "i386", workingDirectory.CombineWithFilePath (i386));
        buildArch ("iphonesimulator", "x86_64", workingDirectory.CombineWithFilePath (x86_64));
        
        buildArch ("iphoneos", "armv7", workingDirectory.CombineWithFilePath (armv7));
        buildArch ("iphoneos", "armv7s", workingDirectory.CombineWithFilePath (armv7s));
        buildArch ("iphoneos", "arm64", workingDirectory.CombineWithFilePath (arm64));
        
		if (!FileExists (workingDirectory.CombineWithFilePath (fatLibrary))) {
            RunLipo (workingDirectory, fatLibrary, new [] {
                (FilePath)i386, 
                (FilePath)x86_64, 
                (FilePath)armv7, 
                (FilePath)armv7s, 
                (FilePath)arm64
            });
        }
    }
});
var DownloadPod = new Action<DirectoryPath, string, string, IDictionary<string, string>> ((podfilePath, platform, platformVersion, pods) => 
{
    if (!IsRunningOnUnix ()) {
        return;
    }
    
    if (!FileExists (podfilePath.CombineWithFilePath ("Podfile.lock"))) {
        var builder = new StringBuilder ();
        builder.AppendFormat ("platform :{0}, '{1}'", platform, platformVersion);
        builder.AppendLine ();
        foreach (var pod in pods) {
            builder.AppendFormat ("pod '{0}', '{1}'", pod.Key, pod.Value);
            builder.AppendLine ();
        }
        
        if (!DirectoryExists (podfilePath)) {
            CreateDirectory (podfilePath);
        }
        
        System.IO.File.WriteAllText (podfilePath.CombineWithFilePath ("Podfile").ToString (), builder.ToString ());
	
        CocoaPodInstall (podfilePath, new CocoaPodInstallSettings {
            NoIntegrate = true
        });
    }
});
var CreateStaticPod = new Action<DirectoryPath, string, string, string, string> ((path, osxVersion, iosVersion, name, version) => {
    if (osxVersion != null) {
        DownloadPod (path.Combine("osx"), 
                    "osx", osxVersion, 
                    new Dictionary<string, string> { { name, version } });
        BuildXCode ("Pods/Pods.xcodeproj", 
                    name,
                    path.Combine ("osx"),
                    true);
    }
    if (iosVersion != null) {
        DownloadPod (path.Combine("ios"), 
                    "ios", iosVersion, 
                    new Dictionary<string, string> { { name, version } });
        BuildXCode ("Pods/Pods.xcodeproj", 
                    name,
                    path.Combine ("ios"),
                    false);
    }
});

var DownloadJar = new Action<string, string, string> ((source, destination, version) =>
{
    source = string.Format("http://search.maven.org/remotecontent?filepath=" + source, version);
    FilePath dest = string.Format(destination, version);
    DirectoryPath destDir = dest.GetDirectory ();
    if (!DirectoryExists (destDir))
        CreateDirectory (destDir);
    if (!FileExists (dest)) {
        DownloadFile (source, dest);
    }
});
var CheckoutGit = new Action<string, string, string> ((source, destination, version) =>
{
    if (!IsRunningOnUnix ()) {
        return;
    }

    DirectoryPath dest = MakeAbsolute ((DirectoryPath) destination);

    if (!DirectoryExists (destination)) {
        RunGit (".", "clone " + source + " " + dest);
    }

    RunGit (destination, "--git-dir=.git checkout " + version);
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// EXTERNALS - 
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("externals")
    .Does (() => 
{
    DownloadJar ("com/squareup/okio/okio/{0}/okio-{0}.jar",
                 "externals/OkIO/okio.jar",
                 okio_version);
    DownloadJar ("com/squareup/okhttp/okhttp/{0}/okhttp-{0}.jar",
                 "externals/OkHttp/okhttp.jar", 
                 okhttp_version);
    DownloadJar ("com/squareup/okhttp3/okhttp/{0}/okhttp-{0}.jar",
                 "externals/OkHttp3/okhttp.jar",
                 okhttp3_version);
    DownloadJar ("com/squareup/okhttp/okhttp-ws/{0}/okhttp-ws-{0}.jar",
                 "externals/OkHttp.WS/okhttp-ws.jar",
                 okhttpws_version);
    DownloadJar ("com/squareup/okhttp3/okhttp-ws/{0}/okhttp-ws-{0}.jar",
                 "externals/OkHttp3.WS/okhttp-ws.jar",
                 okhttp3ws_version);
    DownloadJar ("com/squareup/okhttp/okhttp-urlconnection/{0}/okhttp-urlconnection-{0}.jar",
                 "externals/OkHttp.UrlConnection/okhttp-urlconnection.jar", 
                 okhttpurlconnection_version);
    DownloadJar ("com/squareup/picasso/picasso/{0}/picasso-{0}.jar",
                 "externals/Picasso/picasso.jar", 
                 picasso_version);
    DownloadJar ("com/squareup/android-times-square/{0}/android-times-square-{0}.aar",
                 "externals/AndroidTimesSquare/android-times-square.aar", 
                 androidtimessquare_version);
    DownloadJar ("com/squareup/seismic/{0}/seismic-{0}.jar",
                 "externals/Seismic/seismic.jar", 
                 seismic_version);
    DownloadJar ("com/squareup/pollexor/{0}/pollexor-{0}.jar",
                 "externals/Pollexor/pollexor.jar", 
                 pollexor_version);
    DownloadJar ("com/squareup/retrofit/retrofit/{0}/retrofit-{0}.jar",
                 "externals/Retrofit/retrofit.jar", 
                 retrofit_version);
                
    CreateStaticPod ("externals/SocketRocket/", "10.8", "6.0", "SocketRocket", socketrocket_version);
    CreateStaticPod ("externals/Valet/", "10.10", "6.0", "Valet", valet_version);
    CreateStaticPod ("externals/Aardvark/", null, "6.0", "Aardvark", aardvark_version);
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// LIBS - 
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("libs")
    .IsDependentOn ("externals")
    .Does (() => 
{
	RunNuGetRestore ("./binding/Square.sln");
    DotNetBuild ("./binding/Square.sln", c => {
        c.Configuration = "Release"; 
        // c.Properties ["Platform"] = new [] { "\"Any CPU\"" };
    });
    
    var outputs = new [] {
        "Square.OkIO/bin/Release/Square.OkIO.dll",
        "Square.OkHttp/bin/Release/Square.OkHttp.dll",
        "Square.OkHttp3/bin/Release/Square.OkHttp3.dll",
        "Square.Picasso/bin/Release/Square.Picasso.dll",
        "Square.OkHttp.WS/bin/Release/Square.OkHttp.WS.dll",
        "Square.OkHttp3.WS/bin/Release/Square.OkHttp3.WS.dll",
        "Square.OkHttp.UrlConnection/bin/Release/Square.OkHttp.UrlConnection.dll",
        "Square.SocketRocket/bin/Release/Square.SocketRocket.dll",
        "Square.SocketRocket_OSX/bin/Release/Square.SocketRocket.OSX.dll",
        "Square.AndroidTimesSquare/bin/Release/Square.AndroidTimesSquare.dll",
        "Square.Valet/bin/Release/Square.Valet.dll",
        "Square.Aardvark/bin/Release/Square.Aardvark.dll",
        "Square.Seismic/bin/Release/Square.Seismic.dll",
        "Square.Pollexor/bin/Release/Square.Pollexor.dll",
        "Square.Retrofit/bin/Release/Square.Retrofit.dll",
    };
    
    foreach (var output in outputs) {
        CopyFileToDirectory ("./binding/" + output, "./output/");
    }
    
    CopyFileToDirectory ("README.md", "./output/");
    CopyFileToDirectory ("LICENSE.txt", "./output/");
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// PACKAGING - 
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("nuget")
    .IsDependentOn ("libs")
    .Does (() => 
{
    DeleteFiles ("./output/*.nupkg");
    var nugets = GetFiles ("./nuget/*.nuspec");
    foreach (var nuget in nugets) {
        PackageNuGet (nuget, "./output/");
    }
});

Task ("component")
    .IsDependentOn ("nuget")
    .Does (() => 
{
    DeleteFiles ("./output/*.xam");
    var yamls = GetFiles ("./component/**/component.yaml");
    foreach (var yaml in yamls) {
        var yamlDir = yaml.GetDirectory ();
        PackageComponent (yamlDir, new XamarinComponentSettings { 
            ToolPath = XamarinComponentToolPath
        });
        MoveFiles (yamlDir.FullPath.TrimEnd ('/') + "/*.xam", "./output/");
    }
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// SAMPLES - 
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("samples")
    .IsDependentOn ("libs")
    .Does (() => 
{
    var samples = GetFiles ("./sample/*/*.sln");
    foreach (var sample in samples) {
        RunComponentRestore (sample);
		RunNuGetRestore (sample);
        DotNetBuild (sample, c => {
            c.Configuration = "Release"; 
            // c.Properties ["Platform"] = new [] { "\"Any CPU\"" };
        });
    }
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// CLEAN - 
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("clean")
    .Does (() => 
{
    CleanDirectories ("./binding/*/bin");
    CleanDirectories ("./binding/*/obj");
    CleanDirectories ("./binding/packages");
    CleanDirectories ("./binding/Components");

    CleanDirectories ("./sample/*/bin");
    CleanDirectories ("./sample/*/obj");
    CleanDirectories ("./sample/packages");
    CleanDirectories ("./sample/Components");

    CleanDirectories ("./output");
});

Task ("clean-native")
    .IsDependentOn ("clean")
    .Does (() => 
{
    CleanDirectories("./externals");
});

////////////////////////////////////////////////////////////////////////////////////////////////////
// START - 
////////////////////////////////////////////////////////////////////////////////////////////////////

Task ("CI")
    .IsDependentOn ("externals")
    .IsDependentOn ("libs")
    .IsDependentOn ("nuget")
    .IsDependentOn ("component")
    .IsDependentOn ("samples")
    .Does (() => 
{
});

RunTarget (TARGET);
