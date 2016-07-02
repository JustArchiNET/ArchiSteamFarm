param($installPath, $toolsPath, $package, $project)
$item = $project.ProjectItems | where-object {$_.Name -eq "FodyWeavers.xml"} 
$item.Properties.Item("BuildAction").Value = [int]0