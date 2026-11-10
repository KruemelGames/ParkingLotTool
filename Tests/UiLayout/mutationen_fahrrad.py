"""Mutationen werden nur beim Webpack-Pruefstandsbuild eingesetzt.
Produktionsquellen bleiben unveraendert; abschliessend wird normal gebaut.
"""
from pathlib import Path
import json
import os
import subprocess

hier = Path(__file__).resolve().parent
root = hier.parents[1]
ziel = root / '.codex-build/fahrrad/stufe5-ui-mutationen'
ziel.mkdir(exist_ok=True)
webpack = ['node', '../../UI/node_modules/webpack-cli/bin/cli.js', '--config', 'webpack.config.js']
lauf = ['node', 'run.js', '--sprachen=en-US', '--fall=en-US 1920x1080 skaliert text100 horizontal']
ergebnisse = []


def ausfuehren(cmd, name, env):
    with (ziel / (name + '.log')).open('w', encoding='utf-8') as out:
        p = subprocess.run(cmd, cwd=hier, env=env, stdout=out, stderr=subprocess.STDOUT)
    return p.returncode, (ziel / (name + '.log')).read_text(encoding='utf-8')


try:
    for name in ['edit', 'layout', 'randstrassen', 'schrift']:
        env = dict(os.environ, PLT_UI_MUTATION=name)
        code, log = ausfuehren(webpack, name + '-webpack', env)
        assert code == 0, log
        code, log = ausfuehren(lauf, name, env)
        ok = code == 1 and 'FEHLER' in log and 'UI-LAYOUT:' in log
        ergebnisse.append({'Mutation': name, 'Erkannt': ok})
        print(name, ok, flush=True)
finally:
    env = dict(os.environ)
    env.pop('PLT_UI_MUTATION', None)
    code, log = ausfuehren(webpack, 'normal-webpack', env)
    assert code == 0, log
    code, log = ausfuehren(lauf, 'normal', env)
    print('Normalprobe', code, flush=True)
    assert code == 0, log

(ziel / 'ergebnisse.json').write_text(json.dumps(ergebnisse, indent=2), encoding='utf-8')
assert all(e['Erkannt'] for e in ergebnisse)
