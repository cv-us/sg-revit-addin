# Auto Shorten Flex Pipes

**Command:** `ShortenFlexPipesCommand`
**Domain:** PipeRouting
**Ribbon:** SG Revit Addin > Pipe Routing > Shorten Flex Pipes

## Purpose

Replaces selected flex pipes with new shortest-length flex pipes between the same endpoint elements. Existing flex pipes may have been routed with excess length or slack; this command deletes them and creates direct (minimum-length) connections using the original flex pipe type, preserving all connections to fittings and sprinkler heads.

## Workflow

1. User selects flex pipes (pre-selection or pick prompt)
2. Command analyzes each flex pipe's connections via ConnectorManager
3. Invalid pipes are filtered out (not exactly 2 connections, or connected to PipingSystem objects)
4. User confirms the replacement count
5. Original flex pipes are deleted
6. New shortest-length flex pipes are created between the same endpoint connectors
7. New pipes are connected to the original endpoint elements

## Selection

- **Pre-selection:** If flex pipes are already selected when the command runs, they are used directly
- **Pick mode:** If nothing is selected, the user is prompted to pick flex pipes (filtered to OST_FlexPipeCurves)

## Validation Filters

Each selected flex pipe must pass two checks:

### Exactly 2 Connections
The flex pipe must have exactly 2 connected endpoint elements (one at each end). Pipes with missing or extra connections are skipped.

### No PipingSystem References
Connected elements must be real physical elements (fittings, sprinkler heads, pipe segments), not PipingSystem objects. The command checks the element type directly to filter these out.

## What "Shortening" Means

The command does **not** modify a length parameter. It:
1. **Deletes** the original flex pipe (which may have a circuitous route)
2. **Creates** a brand new flex pipe with just two points (start and end connector origins)
3. **Connects** the new pipe to the same endpoint elements

The result is the shortest possible flex pipe path between the two endpoints — a direct connection.

## Preserved Properties

| Property | How Preserved |
|----------|--------------|
| Flex Pipe Type | Stored from original via `GetTypeId()`, applied to new pipe |
| Piping System Type | Stored from original via `MEPSystem.GetTypeId()` |
| Reference Level | Stored from original, used for new pipe placement |
| Endpoint Connections | Reconnected to the same fitting/head connectors |

## Error Handling

- Pipes that fail deletion are counted and reported
- Pipes where endpoint connectors cannot be found after deletion are counted
- Connection failures (ConnectTo) are caught silently — the new pipe is still created
- Summary dialog reports replaced count, failed count, and skipped count

## Notes

- The command operates in a single transaction for undo support
- After deleting original flex pipes, `Document.Regenerate()` is called to update connector states
- Connector matching uses the connector ID first, with fallback to finding unconnected connectors by proximity
- New flex pipes are created via `FlexPipe.Create` with explicit XYZ points, then reconnected via `Connector.ConnectTo`
