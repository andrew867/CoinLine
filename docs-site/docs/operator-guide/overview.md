# Operator guide overview

Task-oriented flows for provisioning and inspecting terminals — assumes authenticated operator session.

## Session expiry

If an authenticated console request receives HTTP 401, CoinLine clears the current operator session, including the optional API key and cached identity, and returns to the sign-in page. Sign in again to resume work. Requests from an older session cannot sign out a newly authenticated operator. HTTP 403 means the current operator lacks permission for that action; it does not sign the operator out. Unsaved form changes and tab-local download review reminders are not retained after session expiry.

## Workstation field highlighting

Open Settings, then Field highlighting, to choose separate text and background colors for key and mandatory fields. Key fields identify or select records, such as terminal IDs and Configuration IDs. Mandatory fields include required form controls and fields required for populated quick-access keys and screening entries. If both highlights apply, the key-field style takes precedence. Highlighting is optional and does not change validation or downloaded data. Save to apply the settings, or restore defaults to turn both highlights off. The preference stays in this browser across sign-ins.

!!! tip "UI routes"
    See [Frontend routes](../frontend/routes.md).
