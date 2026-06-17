using namespace System.Net

param(
    $Request,
    $TriggerMetadata
)

function New-JsonResponse {
    param(
        [HttpStatusCode]$StatusCode,
        $Body
    )

    $response = [HttpResponseContext]@{
        StatusCode = $StatusCode
        Headers = @{
            'Content-Type' = 'application/json'
        }
    }

    if ($null -ne $Body) {
        $response.Body = $Body | ConvertTo-Json -Depth 10
    }

    return $response
}

function ConvertTo-RequestBodyObject {
    param($Body)

    if ($null -eq $Body) {
        return $null
    }

    if ($Body -is [string]) {
        if ([string]::IsNullOrWhiteSpace($Body)) {
            return $null
        }

        return $Body | ConvertFrom-Json -ErrorAction Stop
    }

    return $Body
}

function Test-BodyProperty {
    param(
        $Body,
        [string]$Name
    )

    if ($null -eq $Body) {
        return $false
    }

    if ($Body -is [hashtable]) {
        return $Body.ContainsKey($Name)
    }

    return $Body.PSObject.Properties.Name -contains $Name
}

function Get-BodyPropertyValue {
    param(
        $Body,
        [string]$Name
    )

    if ($null -eq $Body) {
        return $null
    }

    if ($Body -is [hashtable]) {
        return $Body[$Name]
    }

    $property = $Body.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-WorkflowTestPayload {
    param($Body)

    $requiredProperties = @('source', 'event', 'target', 'message', 'timestamp')
    foreach ($propertyName in $requiredProperties) {
        if (-not (Test-BodyProperty -Body $Body -Name $propertyName)) {
            return $false
        }
    }

    return -not (Test-BodyProperty -Body $Body -Name 'name') -and
        -not (Test-BodyProperty -Body $Body -Name 'employee')
}

function Normalize-NamePart {
    param([string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return ''
    }

    $letters = [regex]::Matches($Value, '\p{L}') | ForEach-Object { $_.Value }
    return -join $letters
}

try {
    $body = ConvertTo-RequestBodyObject -Body $Request.Body
}
catch {
    Push-OutputBinding -Name Response -Value (New-JsonResponse -StatusCode ([HttpStatusCode]::BadRequest) -Body @{
        error = 'Request body must be valid JSON.'
    })
    return
}

if (Test-WorkflowTestPayload -Body $body) {
    Write-Information 'Received workflow test payload. Returning 200 without processing.'
    Push-OutputBinding -Name Response -Value (New-JsonResponse -StatusCode ([HttpStatusCode]::OK) -Body $null)
    return
}

$employee = if (Test-BodyProperty -Body $body -Name 'employee') {
    Get-BodyPropertyValue -Body $body -Name 'employee'
}
else {
    $body
}

$name = Normalize-NamePart -Value (Get-BodyPropertyValue -Body $employee -Name 'name')

if ($name.Length -lt 3) {
    Push-OutputBinding -Name Response -Value (New-JsonResponse -StatusCode ([HttpStatusCode]::BadRequest) -Body @{
        error = 'name must contain at least 3 letters.'
    })
    return
}

$employeeId = ('{0}{1:00}' -f $name.Substring(0, 3), [System.Security.Cryptography.RandomNumberGenerator]::GetInt32(0, 100)).ToUpperInvariant()

Write-Information ('Generated employee ID {0} for {1}.' -f $employeeId, $name)

Push-OutputBinding -Name Response -Value (New-JsonResponse -StatusCode ([HttpStatusCode]::OK) -Body @{
    employeeId = $employeeId
    name = $name
})