param(
    [Parameter(Mandatory = $true)]
    [string] $Path,

    [Parameter(Mandatory = $true)]
    [string] $CanonicalPath
)

$ErrorActionPreference = 'Stop'

function Assert-PortableCondition {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Convert-PlistDictionary {
    param([Parameter(Mandatory)] [System.Xml.XmlElement] $Dictionary)

    $result = @{}
    $children = @($Dictionary.ChildNodes | Where-Object NodeType -eq Element)
    for ($index = 0; $index -lt $children.Count; $index += 2) {
        $result[$children[$index].InnerText] = $children[$index + 1]
    }

    return $result
}

function Get-RootDictionary {
    param([Parameter(Mandatory)] [xml] $Plist)

    return $Plist.plist.dict
}

function Get-Actions {
    param([Parameter(Mandatory)] [xml] $Plist)

    $root = Get-RootDictionary $Plist
    $array = $root.SelectSingleNode("key[text()='WFWorkflowActions']/following-sibling::*[1][self::array]")
    Assert-PortableCondition ($null -ne $array) 'Shortcut artifact is missing its actions array.'
    return @($array.dict)
}

function Get-ActionByOutputName {
    param(
        [Parameter(Mandatory)] [System.Xml.XmlElement[]] $Actions,
        [Parameter(Mandatory)] [string] $OutputName
    )

    foreach ($actionNode in $Actions) {
        $action = Convert-PlistDictionary $actionNode
        if (-not $action.ContainsKey('WFWorkflowActionParameters')) {
            continue
        }

        $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
        if ($parameters.ContainsKey('CustomOutputName') -and
            $parameters.CustomOutputName.InnerText -eq $OutputName) {
            return $actionNode
        }
    }

    throw "Shortcut artifact is missing action output '$OutputName'."
}

function Assert-PortableFolder {
    param(
        [Parameter(Mandatory)] [System.Xml.XmlElement] $Folder,
        [Parameter(Mandatory)] [string] $Label
    )

    Assert-PortableCondition ($Folder.Name -eq 'dict') "$Label must use a built-in relative folder dictionary."
    $folderValue = Convert-PlistDictionary $Folder
    Assert-PortableCondition ($folderValue.displayName.InnerText -eq 'Game Reminders') "$Label has the wrong display name."
    Assert-PortableCondition ($folderValue.filename.InnerText -eq 'Game Reminders') "$Label has the wrong folder name."
    Assert-PortableCondition ($folderValue.fileLocation.Name -eq 'dict') "$Label is missing its relative file location."
    $location = Convert-PlistDictionary $folderValue.fileLocation
    Assert-PortableCondition ($location.relativeSubpath.InnerText -eq 'Game Reminders') "$Label is not relative to the built-in Shortcuts folder."
    Assert-PortableCondition (-not $location.ContainsKey('WFFileLocationType')) "$Label must not select a macOS-only home location."
    Assert-PortableCondition (-not $folderValue.ContainsKey('bookmarkData')) "$Label must not embed a device-specific bookmark."
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
    -not (Test-Path -LiteralPath $CanonicalPath -PathType Leaf)) {
    throw 'Portable or canonical Shortcut artifact was not found.'
}

$portableRaw = Get-Content -LiteralPath $Path -Raw
Assert-PortableCondition ($portableRaw -notmatch '(?i)bookmark|security.?scoped') 'Portable Shortcut contains bookmark metadata.'

[xml] $portable = $portableRaw
[xml] $canonical = Get-Content -LiteralPath $CanonicalPath -Raw
$portableRoot = Get-RootDictionary $portable
$canonicalRoot = Get-RootDictionary $canonical
$portableActions = @(Get-Actions $portable)
$canonicalActions = @(Get-Actions $canonical)
Assert-PortableCondition ($portableActions.Count -eq 107) "Expected 107 portable Shortcut actions, found $($portableActions.Count)."
Assert-PortableCondition ($portableActions.Count -eq $canonicalActions.Count) 'Portable and canonical action counts differ.'

$portableQuestions = $portableRoot.SelectSingleNode("key[text()='WFWorkflowImportQuestions']/following-sibling::*[1][self::array]")
$canonicalQuestions = $canonicalRoot.SelectSingleNode("key[text()='WFWorkflowImportQuestions']/following-sibling::*[1][self::array]")
Assert-PortableCondition ($null -ne $portableQuestions -and @($portableQuestions.SelectNodes('dict')).Count -eq 0) 'Portable Shortcut must not contain import questions.'
Assert-PortableCondition ($null -ne $canonicalQuestions -and @($canonicalQuestions.SelectNodes('dict')).Count -eq 2) 'Canonical comparison artifact must retain two folder questions.'

$firstAction = Convert-PlistDictionary $portableActions[0]
$firstParameters = Convert-PlistDictionary $firstAction.WFWorkflowActionParameters
Assert-PortableCondition ($firstAction.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.documentpicker.open') 'Portable catalog action has the wrong identifier.'
Assert-PortableCondition ($firstParameters.WFGetFilePath.InnerText -eq 'games.json') 'Portable catalog action has the wrong relative path.'
Assert-PortableFolder $firstParameters.WFFile 'Catalog folder'

$inboxActionNode = Get-ActionByOutputName $portableActions 'inboxFolder'
$inboxAction = Convert-PlistDictionary $inboxActionNode
$inboxParameters = Convert-PlistDictionary $inboxAction.WFWorkflowActionParameters
Assert-PortableCondition ($inboxAction.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.documentpicker.open') 'Portable inbox action has the wrong identifier.'
Assert-PortableCondition ($inboxParameters.WFGetFilePath.InnerText -eq 'inbox') 'Portable inbox action has the wrong relative path.'
Assert-PortableFolder $inboxParameters.WFFile 'Inbox folder'

$unsupportedActions = @($portableActions | Where-Object {
    $action = Convert-PlistDictionary $_
    $action.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.getparentdirectory'
})
Assert-PortableCondition ($unsupportedActions.Count -eq 0) 'Portable Shortcut contains the macOS-only Get Parent Directory action.'

# Prove that the prototype differs from the reviewed artifact only in the two
# folder values and import questions. Normalize those three nodes, then compare
# the complete XML documents.
$canonicalFirstAction = Convert-PlistDictionary $canonicalActions[0]
$canonicalFirstParameters = Convert-PlistDictionary $canonicalFirstAction.WFWorkflowActionParameters
[void] $firstParameters.WFFile.ParentNode.ReplaceChild(
    $portable.ImportNode($canonicalFirstParameters.WFFile, $true),
    $firstParameters.WFFile)

$canonicalInboxNode = Get-ActionByOutputName $canonicalActions 'inboxFolder'
$canonicalInbox = Convert-PlistDictionary $canonicalInboxNode
$canonicalInboxParameters = Convert-PlistDictionary $canonicalInbox.WFWorkflowActionParameters
[void] $inboxParameters.WFFile.ParentNode.ReplaceChild(
    $portable.ImportNode($canonicalInboxParameters.WFFile, $true),
    $inboxParameters.WFFile)

[void] $portableQuestions.ParentNode.ReplaceChild(
    $portable.ImportNode($canonicalQuestions, $true),
    $portableQuestions)

Assert-PortableCondition ($portable.OuterXml -eq $canonical.OuterXml) 'Portable prototype changes more than folder resolution and import questions.'

Write-Host 'Portable Shortcut prototype is structurally valid: 107 unchanged actions, two built-in relative folder references, no import questions, no bookmark metadata, and no macOS-only parent action.'
