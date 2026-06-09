# challenge-artifact

## Overview
As a Senior Reviewer, conduct an adversarial critique of a specified artifact (spec, design, epics, or similar) to surface hidden assumptions, contradictions, gaps, and cross-reference mismatches with upstream artifacts. This workflow produces severity-ranked findings and a clear verdict of APPROVE, NEEDS ATTENTION, or REJECT.

## Execution
Call MCP tool:
    - ReadPrompt(name="challenge-artifact", version="latest")

- Update ToDo list derived from the returned prompt instructions by readjusting the items.
