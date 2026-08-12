param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Shortcut artifact not found: $Path"
}

$rawArtifact = Get-Content -LiteralPath $Path -Raw
if ($rawArtifact -match '(?i)bookmark|security.?scoped') {
    throw 'Shortcut artifact contains device-specific bookmark metadata.'
}

[xml]$plist = $rawArtifact
$rootDictionary = $plist.plist.dict
$actionsArray = $rootDictionary.SelectSingleNode("key[text()='WFWorkflowActions']/following-sibling::*[1][self::array]")
$questionsArray = $rootDictionary.SelectSingleNode("key[text()='WFWorkflowImportQuestions']/following-sibling::*[1][self::array]")

if ($null -eq $actionsArray -or $null -eq $questionsArray) {
    throw "Shortcut artifact is missing actions or import questions."
}

$actions = @($actionsArray.dict)
if ($actions.Count -ne 76) {
    throw "Expected 76 Shortcut actions, found $($actions.Count)."
}

function Convert-PlistDictionary {
    param([System.Xml.XmlElement]$Dictionary)

    $result = @{}
    $children = @($Dictionary.ChildNodes | Where-Object NodeType -eq Element)
    for ($index = 0; $index -lt $children.Count; $index += 2) {
        $result[$children[$index].InnerText] = $children[$index + 1]
    }

    return $result
}

$firstAction = Convert-PlistDictionary $actions[0]
$firstParameters = Convert-PlistDictionary $firstAction.WFWorkflowActionParameters
if ($firstAction.WFWorkflowActionIdentifier.InnerText -ne "is.workflow.actions.documentpicker.open" -or
    $firstParameters.WFGetFilePath.InnerText -ne "Game Reminders/games.json" -or
    $firstParameters.ContainsKey("WFFile")) {
    throw "The first action must read Game Reminders/games.json directly from the built-in Shortcuts container."
}

$unsupportedActions = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    $action.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.getparentdirectory'
})
if ($unsupportedActions.Count -ne 0) {
    throw 'The generated iPhone Shortcut contains the macOS-only Get Parent Directory action.'
}

$questions = @($questionsArray.SelectNodes('dict'))
if ($questions.Count -ne 0) {
    throw "Expected no folder import questions for the cross-device Shortcut, found $($questions.Count)."
}

$chooseActions = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    $action.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.choosefromlist'
})
if ($chooseActions.Count -ne 1) {
    throw "The compiled Shortcut must contain exactly one native Choose from List action."
}
$choose = Convert-PlistDictionary $chooseActions[0]
$chooseParameters = Convert-PlistDictionary $choose.WFWorkflowActionParameters
if ($chooseParameters.WFChooseFromListActionPrompt.InnerText -ne 'Choose a game' -or
    $chooseParameters.WFChooseFromListActionSelectMultiple.Name -ne 'false' -or
    (Convert-PlistDictionary (Convert-PlistDictionary $chooseParameters.WFInput).Value).OutputName.InnerText -ne 'gameNames') {
    throw 'Choose from List must immediately show all repeated canonical names with single selection enabled.'
}

$askActions = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    $action.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.ask'
})
if ($askActions.Count -ne 1) {
    throw 'The compiled Shortcut must ask only for the reminder message; game-name text input is forbidden.'
}
$ask = Convert-PlistDictionary $askActions[0]
$askParameters = Convert-PlistDictionary $ask.WFWorkflowActionParameters
if ($askParameters.WFAskActionPrompt.InnerText -ne 'What should I remind you?') {
    throw 'The only text prompt must ask What should I remind you?.'
}

function Get-ActionByOutputName {
    param([Parameter(Mandatory)] [string] $OutputName)

    foreach ($actionNode in $actions) {
        $action = Convert-PlistDictionary $actionNode
        $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
        if ($parameters.ContainsKey('CustomOutputName') -and $parameters.CustomOutputName.InnerText -eq $OutputName) {
            return $actionNode
        }
    }

    throw "Shortcut artifact is missing action output '$OutputName'."
}

function Get-ReferencedOutputName {
    param([Parameter(Mandatory)] [System.Xml.XmlElement] $Parameter)

    $attachment = Convert-PlistDictionary $Parameter
    $value = Convert-PlistDictionary $attachment.Value
    return $value.OutputName.InnerText
}

$matchCountInitializers = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    if ($action.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.number') { return $false }
    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    $parameters.ContainsKey('WFNumberActionNumber') -and $parameters.WFNumberActionNumber.InnerText -eq '0'
})
if ($matchCountInitializers.Count -ne 1) {
    throw 'Selection resolution must initialize matchCount to zero.'
}
$selectionGuard = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    if ($action.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.conditional') { return $false }
    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    $parameters.ContainsKey('WFCondition') -and
        $parameters.WFCondition.InnerText -eq '5' -and
        $parameters.ContainsKey('WFNumberValue') -and
        $parameters.WFNumberValue.InnerText -eq '1' -and
        $parameters.WFInput.InnerText -match 'matchCount'
})
if ($selectionGuard.Count -ne 1) {
    throw 'Canceled, missing, and duplicate game selections must be rejected unless matchCount equals one.'
}

$inboxActionNode = Get-ActionByOutputName 'inboxFolder'
$saveActionNode = Get-ActionByOutputName 'stagedFile'
$moveActionNode = Get-ActionByOutputName 'inboxStagedFile'
$renameActionNode = Get-ActionByOutputName 'savedReminder'
$inboxAction = Convert-PlistDictionary $inboxActionNode
$saveAction = Convert-PlistDictionary $saveActionNode
$moveAction = Convert-PlistDictionary $moveActionNode
$renameAction = Convert-PlistDictionary $renameActionNode
$inboxParameters = Convert-PlistDictionary $inboxAction.WFWorkflowActionParameters
$saveParameters = Convert-PlistDictionary $saveAction.WFWorkflowActionParameters
$moveParameters = Convert-PlistDictionary $moveAction.WFWorkflowActionParameters
$renameParameters = Convert-PlistDictionary $renameAction.WFWorkflowActionParameters

if ($inboxAction.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.file.createfolder' -or
    $inboxParameters.WFFilePath.InnerText -ne 'Game Reminders/inbox' -or
    $inboxParameters.ContainsKey('WFFile') -or
    $inboxParameters.ContainsKey('WFFileErrorIfNotFound')) {
    throw 'The Shortcut must create or reuse Game Reminders/inbox in the built-in Shortcuts container.'
}

$stagingPath = Convert-PlistDictionary (Convert-PlistDictionary $saveParameters.WFFileDestinationPath).Value
if ($saveAction.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.documentpicker.save' -or
    $saveParameters.ContainsKey('WFFile') -or
    $saveParameters.WFAskWhereToSave.Name -ne 'false' -or
    $stagingPath.string.InnerText -notmatch '^\uFFFC\.tmp$' -or
    (Get-ReferencedOutputName $saveParameters.WFInput) -ne 'reminderJson') {
    throw 'The reminder must be written as a visible UUID-named temporary file in the private Shortcuts staging folder.'
}

if ($moveAction.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.file.move' -or
    (Get-ReferencedOutputName $moveParameters.WFFile) -ne 'stagedFile' -or
    (Get-ReferencedOutputName $moveParameters.WFFolder) -ne 'inboxFolder') {
    throw 'The completed temporary file must move to the resolved inbox folder.'
}

if ($renameAction.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.file.rename' -or
    (Get-ReferencedOutputName $renameParameters.WFFile) -ne 'inboxStagedFile') {
    throw 'The temporary file must be renamed only after it reaches inbox.'
}

$finalFilename = Convert-PlistDictionary (Convert-PlistDictionary $renameParameters.WFNewFilename).Value
if ($finalFilename.string.InnerText -notmatch '^\uFFFC\.json$' -or
    $finalFilename.string.InnerText.StartsWith('.')) {
    throw 'The final reminder must have a visible UUID-named JSON filename.'
}

$inboxIndex = [Array]::IndexOf($actions, $inboxActionNode)

$saveIndex = [Array]::IndexOf($actions, $saveActionNode)
$moveIndex = [Array]::IndexOf($actions, $moveActionNode)
$renameIndex = [Array]::IndexOf($actions, $renameActionNode)
if (-not ($inboxIndex -lt $saveIndex -and $saveIndex -lt $moveIndex -and $moveIndex -lt $renameIndex)) {
    throw 'Expected inbox creation/reuse, staging save, inbox move, and final rename in that order.'
}

$controlFlowGroups = @{}
foreach ($actionNode in $actions) {
    $action = Convert-PlistDictionary $actionNode
    $identifier = $action.WFWorkflowActionIdentifier.InnerText
    if ($identifier -notin @("is.workflow.actions.conditional", "is.workflow.actions.repeat.each")) {
        continue
    }

    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    $group = $parameters.GroupingIdentifier.InnerText
    if (-not $controlFlowGroups.ContainsKey($group)) {
        $controlFlowGroups[$group] = @()
    }

    $controlFlowGroups[$group] += [int]$parameters.WFControlFlowMode.InnerText
}

if ($controlFlowGroups.Count -ne 6) {
    throw "Expected 6 distinct control-flow groups, found $($controlFlowGroups.Count). Do not compile with Cherri's --derive-uuids option."
}

foreach ($entry in $controlFlowGroups.GetEnumerator()) {
    $modes = @($entry.Value | Sort-Object)
    if ($modes.Count -ne 2 -or $modes[0] -ne 0 -or $modes[1] -ne 2) {
        throw "Control-flow group $($entry.Key) is not a single balanced start/end pair."
    }
}

$actionUuids = @()
foreach ($actionNode in $actions) {
    $action = Convert-PlistDictionary $actionNode
    if (-not $action.ContainsKey('WFWorkflowActionParameters')) {
        continue
    }

    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    if ($parameters.ContainsKey('UUID')) {
        $actionUuids += $parameters.UUID.InnerText
    }
}

$duplicateActionUuids = @($actionUuids | Group-Object | Where-Object Count -gt 1)
if ($duplicateActionUuids.Count -ne 0) {
    throw "Shortcut artifact contains duplicate action UUIDs."
}

$forbiddenNameFlowActions = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    $identifier = $action.WFWorkflowActionIdentifier.InnerText
    if ($identifier -eq 'is.workflow.actions.text.changecase') { return $true }
    if ($identifier -eq 'is.workflow.actions.text.replace') {
        $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
        return $parameters.WFReplaceTextFind.InnerText -like '*p{L}*'
    }
    return $false
})
if ($forbiddenNameFlowActions.Count -ne 0) {
    throw 'The compiled Shortcut must not parse or normalize game-name text.'
}

$missingVariableInputs = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    if ($action.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.setvariable') {
        return $false
    }

    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    -not $parameters.ContainsKey('WFInput')
})
if ($missingVariableInputs.Count -ne 0) {
    throw 'Every Set Variable action must have an explicit input; do not rely on Nothing to clear per-game state.'
}

Write-Host "Shortcut artifact is structurally valid: 76 Cherri-generated iPhone-compatible actions, a native single-selection canonical-name list, exactly-one stable-ID resolution, explicit variable inputs, unique action IDs, exact visible Shortcuts paths for Game Reminders/games.json and Game Reminders/inbox without bookmarks or import questions, visible staging/final filenames, anchored inbox move, and 6 unique balanced control-flow groups."
