param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Shortcut artifact not found: $Path"
}

[xml]$plist = Get-Content -LiteralPath $Path -Raw
$rootDictionary = $plist.plist.dict
$actionsArray = $rootDictionary.SelectSingleNode("key[text()='WFWorkflowActions']/following-sibling::*[1][self::array]")
$questionsArray = $rootDictionary.SelectSingleNode("key[text()='WFWorkflowImportQuestions']/following-sibling::*[1][self::array]")

if ($null -eq $actionsArray -or $null -eq $questionsArray) {
    throw "Shortcut artifact is missing actions or import questions."
}

$actions = @($actionsArray.dict)
if ($actions.Count -ne 107) {
    throw "Expected 107 Shortcut actions, found $($actions.Count)."
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
    $firstParameters.WFGetFilePath.InnerText -ne "games.json" -or
    -not $firstParameters.ContainsKey("WFFile")) {
    throw "The first action is not configured for the Game Reminders folder import question."
}

$unsupportedActions = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    $action.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.getparentdirectory'
})
if ($unsupportedActions.Count -ne 0) {
    throw 'The generated iPhone Shortcut contains the macOS-only Get Parent Directory action.'
}

$questions = @($questionsArray.dict)
if ($questions.Count -ne 2) {
    throw "Expected exactly two folder import questions, found $($questions.Count)."
}

$question = Convert-PlistDictionary $questions[0]
if ($question.ActionIndex.InnerText -ne "0" -or $question.ParameterKey.InnerText -ne "WFFile") {
    throw "The catalog folder import question does not target the first action's WFFile parameter."
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

if ($inboxAction.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.documentpicker.open' -or
    $inboxParameters.WFGetFilePath.InnerText -ne 'inbox' -or
    -not $inboxParameters.ContainsKey('WFFile') -or
    $inboxParameters.WFFile.Name -ne 'string' -or
    $inboxParameters.WFFile.InnerText.Length -ne 0) {
    throw 'The inbox lookup must expose an empty folder parameter for its own import question.'
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
$inboxQuestion = Convert-PlistDictionary $questions[1]
if ($inboxQuestion.ActionIndex.InnerText -ne "$inboxIndex" -or $inboxQuestion.ParameterKey.InnerText -ne 'WFFile') {
    throw "The inbox folder import question does not target action $inboxIndex's WFFile parameter."
}

$saveIndex = [Array]::IndexOf($actions, $saveActionNode)
$moveIndex = [Array]::IndexOf($actions, $moveActionNode)
$renameIndex = [Array]::IndexOf($actions, $renameActionNode)
if (-not ($inboxIndex -lt $saveIndex -and $saveIndex -lt $moveIndex -and $moveIndex -lt $renameIndex)) {
    throw 'Expected inbox resolution, staging save, inbox move, and final rename in that order.'
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

if ($controlFlowGroups.Count -ne 11) {
    throw "Expected 11 distinct control-flow groups, found $($controlFlowGroups.Count). Do not compile with Cherri's --derive-uuids option."
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

$replaceActions = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    $action.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.text.replace'
})
$normalizationPatterns = @($replaceActions | ForEach-Object {
    $action = Convert-PlistDictionary $_
    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    $parameters.WFReplaceTextFind.InnerText
} | Where-Object { $_ -like '*p{L}*' })
if ($normalizationPatterns.Count -ne 3 -or
    @($normalizationPatterns | Where-Object { $_ -cne '[^\p{L}\p{N}]' }).Count -ne 0) {
    throw 'The compiled Shortcut must contain exactly three single-escaped Unicode name-normalization patterns.'
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

$numberActions = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    $action.WFWorkflowActionIdentifier.InnerText -eq 'is.workflow.actions.number'
})
$hasZeroInitializer = $false
foreach ($numberActionNode in $numberActions) {
    $action = Convert-PlistDictionary $numberActionNode
    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    if ($parameters.ContainsKey('WFNumberActionNumber') -and $parameters.WFNumberActionNumber.InnerText -eq '0') {
        $hasZeroInitializer = $true
    }
}
if (-not $hasZeroInitializer) {
    throw 'The compiled Shortcut is missing the numeric zero match-count initializer.'
}

$textInitializers = @($actions | Where-Object {
    $action = Convert-PlistDictionary $_
    if ($action.WFWorkflowActionIdentifier.InnerText -ne 'is.workflow.actions.gettext') {
        return $false
    }

    $parameters = Convert-PlistDictionary $action.WFWorkflowActionParameters
    if (-not $parameters.ContainsKey('WFTextActionText')) {
        return $false
    }

    $textNode = $parameters.WFTextActionText
    if ($textNode.Name -eq 'string') {
        return $textNode.InnerText -eq 'NO_MATCH'
    }

    $textValue = Convert-PlistDictionary $textNode
    if (-not $textValue.ContainsKey('Value')) {
        return $false
    }

    $value = Convert-PlistDictionary $textValue.Value
    $value.ContainsKey('string') -and $value.string.InnerText -eq 'NO_MATCH'
})
if ($textInitializers.Count -ne 2) {
    throw 'The compiled Shortcut must initialize both matched-game text variables explicitly.'
}

Write-Host "Shortcut artifact is structurally valid: 107 iPhone-compatible actions, exact normalization, explicit variable inputs, unique action IDs, two folder questions, visible staging/final filenames, anchored inbox move, and 11 unique balanced control-flow groups."
