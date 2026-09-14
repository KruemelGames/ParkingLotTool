from pathlib import Path
import tempfile, subprocess, json, sys
from playwright.sync_api import sync_playwright
repo=Path(__file__).resolve().parents[2]; out=Path(tempfile.gettempdir())/'plt-vegetation-browser';out.mkdir(exist_ok=True)
subprocess.run([sys.executable,str(repo/'Tests/ParkplatzListe/browser_setup.py')],check=True)
base=Path(tempfile.gettempdir())/'plt-liste-browser'
# Vorhandene CS2-Test-Doubles; UI rendert echten Vegetationscode und SCSS.
for name in ['ui.js','index.html']:(out/name).write_text((base/name).read_text(encoding='utf-8'),encoding='utf-8')
api=(base/'api.js').read_text(encoding='utf-8').replace(' window.calls.push({key,payload});',''' window.calls.push({key,payload});
 if(key==='SetVegetation')set('Vegetation',payload);
 if(key==='SaveVegetationSet'){const c=JSON.parse(data.get('VegetationCatalog'));c.Sets.push({...JSON.parse(payload),Id:'custom:1',Custom:true});set('VegetationCatalog',JSON.stringify(c));}
 if(key==='DeleteVegetationSet'){const c=JSON.parse(data.get('VegetationCatalog'));c.Sets=c.Sets.filter(s=>s.Id!==payload);set('VegetationCatalog',JSON.stringify(c));}
''');(out/'api.js').write_text(api,encoding='utf-8')
component=str(repo/'UI/src/mods/vegetation').replace('\\','/')
(out/'entry.tsx').write_text('import React from "react";import {createRoot} from "react-dom/client";import {Vegetation} from '+json.dumps(component)+';createRoot(document.getElementById("root")).render(<div style={{width:"340rem",padding:"16rem",background:"#172232",color:"white"}}><Vegetation/></div>);')
(out/'webpack.cjs').write_text((base/'webpack.cjs').read_text(encoding='utf-8'))
subprocess.run(['node',str(repo/'UI/node_modules/webpack-cli/bin/cli.js'),'--config',str(out/'webpack.cjs'),'--stats','errors-only'],check=True,cwd=repo)
with sync_playwright() as p:
 b=p.chromium.launch();page=b.new_page(viewport={'width':700,'height':1000});page.goto((out/'index.html').as_uri());page.wait_for_selector('[role=switch]')
 assert page.locator('button').count()==1
 assets=[{'Id':str(i),'Name':('Baum ' if i%2 else 'Busch ')+str(i),'Icon':'','Tree':bool(i%2)} for i in range(85)]
 sets=[{'Id':'deciduous','Name':'Wilde Laubbäume','Species':['1','3'],'Icon':''},{'Id':'bushes','Name':'Wilde Büsche','Species':['0','2'],'Icon':''}]
 page.evaluate('(c)=>window.testSet("VegetationCatalog",JSON.stringify(c))',{'Assets':assets,'Sets':sets})
 page.get_by_role('switch').click();page.get_by_role('button',name='Line',exact=True).click()
 page.get_by_role('button',name='Wilde Laubbäume',exact=True).click();page.get_by_role('button',name='Wilde Büsche',exact=True).click()
 page.get_by_role('button',name='Alt',exact=True).click()
 page.get_by_role('button',name='Tot',exact=True).click()
 page.get_by_role('button',name='Baumstumpf',exact=True).click()
 assert page.locator('button[aria-pressed] span').count()==0
 assert page.locator('button[aria-pressed] img').count()>=10
 page.get_by_role('button',name='Pflanzen auswählen / eigenes Set',exact=True).click()
 page.get_by_role('button',name='Weitere Pflanzen',exact=True).click();assert page.get_by_role('button',name='Busch 84',exact=True).count()==1
 page.get_by_label('Pflanzen suchen',exact=True).fill('Baum 83');page.get_by_role('button',name='Baum 83',exact=True).click()
 page.get_by_label('Setname',exact=True).fill('Mein Stadtgrün');page.get_by_role('button',name='Speichern',exact=True).click()
 assert page.get_by_role('button',name='Mein Stadtgrün',exact=True).count()==1
 last=page.evaluate('window.calls.filter(x=>x.key==="SetVegetation").slice(-1)[0].payload');v=json.loads(last)
 assert v['Line'] and v['Ages']==62 and len(v['Species'])==5,v
 # Echte installierte SVGs fuer die Browser-Abnahme laden (Cohtml loest diese URLs im Spiel auf).
 page.evaluate("""()=>{for(const img of document.querySelectorAll('button img')){
  const src=img.getAttribute('src');
  if(src.startsWith('coui://uil/Standard/'))img.src='file:///C:/Users/kruem/AppData/LocalLow/Colossal Order/Cities Skylines II/.cache/Mods/pdx_mods/74417_17/Icons/Standard/'+src.split('/').pop();
  else if(src.startsWith('Media/'))img.src='file:///F:/Games/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Content/Game/UI/'+src;
 }}""")
 page.wait_for_function('()=>Array.from(document.querySelectorAll("button img")).every(i=>i.complete&&i.naturalWidth>0)')
 page.screenshot(path=str(out/'vegetation.png'))
 for width in [180,240,340]:
  page.evaluate('(w)=>document.querySelector("#root > div").style.width=w+"rem"',width)
  assert page.evaluate('()=>{const e=document.querySelector("#root > div");return e.scrollWidth<=e.clientWidth+2}'),width

 page.get_by_role('switch').click();assert page.locator('button').count()==1
 print('Vegetations-UI: aus/ein, Modi, Alter, kombinierte Sets, Suche, weitere Assets, eigene Sets, Einklappen bestanden.')
 b.close()
