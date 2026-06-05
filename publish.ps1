param(
  [ValidateSet('win-x64','linux-x64')]
  [string]$Runtime = 'win-x64'
)

$project = "src/DotNetInspectorMcp/DotNetInspectorMcp.csproj"
$profile = if ($Runtime -eq 'win-x64') { 'win-x64' } else { 'linux-x64' }
$assemblyName = "DnSpyMCP-$Runtime"

dotnet publish $project -c Release -p:PublishProfile=$profile -p:AssemblyName=$assemblyName
