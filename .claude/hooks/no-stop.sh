#!/bin/bash
R="$(cd "$(dirname "$0")/../.." && pwd)"
[ -f "$R/.claude/ALLOW_STOP" ] && exit 0
pmset -g therm 2>/dev/null | grep -qiE 'warning level.*[1-9]|CPU_Speed_Limit *= *[0-8][0-9]?$' && exit 0
left=$(python3 -I -c "import json,sys;d=json.load(open(sys.argv[1]))['phases'];print(','.join(k for k,v in d.items() if v!='done'))" "$R/artifacts/refactor/state.json" 2>/dev/null)
[ -z "$left" ] && exit 0
printf '{"decision":"block","reason":"Refactor not finished (phases left: %s). Continue with the next step of docs/refactor-plan.md; update artifacts/refactor/state.json when a phase is done. Stop only for thermal warning or a decision that truly needs the owner (then create .claude/ALLOW_STOP and say why)."}\n' "$left"
