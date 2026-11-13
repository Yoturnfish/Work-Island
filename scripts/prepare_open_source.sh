#!/bin/sh
# Export only public inputs into a fresh checkout. Private history stays in place.
set -eu
PROJECT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
DESTINATION=${1:?Usage: sh scripts/prepare_open_source.sh /absolute/new-directory}
case "$DESTINATION" in /*) ;; *) echo 'Destination must be absolute' >&2; exit 1 ;; esac
if [ -e "$DESTINATION" ] || [ -L "$DESTINATION" ]; then
    echo 'Destination already exists; nothing will be overwritten' >&2
    exit 1
fi
python3 - "$PROJECT_DIR" "$DESTINATION" <<'PY'
from pathlib import Path
import shutil, sys
root, dest = map(Path, sys.argv[1:])
items = ['Package.swift', '.gitignore', 'AGENTS.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md',
         'README.md', 'README.zh-CN.md', 'CONTRIBUTING.md', 'SECURITY.md', '使用教程.html', 'AppResources',
         'Sources', 'Tests', 'Specs', '.github', 'windows', 'docs/ARCHITECTURE.md', 'docs/VIDEO.zh-CN.md', 'docs/assets/work-island-overview.png',
         'scripts/test_app.sh', 'scripts/package_app.sh', 'scripts/update_app.sh',
         'scripts/restart_app.swift', 'scripts/prepare_open_source.sh']
excluded = {'.DS_Store', 'bin', 'obj', '.cache', '.vs', 'artifacts', 'test-results',
            'backups', 'Data', 'PreviewData', '.git', '__pycache__'}
def ignored(path):
    return (any(p in excluded for p in path.parts) or path.name.startswith(('._', '.env', 'state.json'))
            or path.suffix.lower() in {'.log', '.pfx', '.p12', '.p8', '.mobileprovision', '.user', '.suo'})
files = []
for item in items:
    source = root / item
    if not source.exists():
        raise SystemExit('Missing public input: ' + item)
    for path in sorted(source.rglob('*')) if source.is_dir() else [source]:
        relative = path.relative_to(root)
        if ignored(relative):
            continue
        if path.is_symlink():
            raise SystemExit('Review symlink before export: ' + str(relative))
        if path.is_file():
            files.append((path, relative))
dest.mkdir(parents=True)
for source, relative in files:
    target = dest / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
changelog = (root / 'CHANGELOG.md').read_text()
latest = changelog.split('\n## ', 2)[1]
(dest / 'CHANGELOG.md').write_text('# Work Island 更新记录\n\n## ' + latest.rstrip() +
    '\n\n## Windows 0.1.24\n\n- Windows 首轮开源版本：工作计时、规则、历史、主题、悬浮窗及系统托盘。\n')
print(f'Exported {len(files) + 1} public files')
PY
git -C "$DESTINATION" init -b main
git -C "$DESTINATION" add -- .
git -C "$DESTINATION" -c user.name='Yoturn' -c user.email='321509051+Yoturnfish@users.noreply.github.com' -c commit.gpgsign=false commit -m 'chore: prepare macOS and Windows source'
echo "Prepared local checkout: $DESTINATION"
