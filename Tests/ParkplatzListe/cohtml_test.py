from pathlib import Path
import tempfile
from playwright.sync_api import sync_playwright
out=Path(tempfile.gettempdir())/'plt-liste-browser'
with sync_playwright() as p:
 browser=p.chromium.launch()
 page=browser.new_page(viewport={'width':1920,'height':1080})
 page.goto((out/'index.html').as_uri())
 page.wait_for_selector('.listeKachel')
 page.evaluate('''()=>{window.testSet('Sprache','en');window.testSet('ParkplatzListe',[
 ['1:1','Parkplatz (129 Stellplätze)',129,0,10,3315,0,123,87,0,-3315,0],
 ['2:1','Parkplatz (132 Stellplätze)',132,30,15,3387,0,88,103,0,-3387,0]
 ].map(r=>r.join('\\t')).join('\\n'));}''')
 page.wait_for_timeout(100)
 selectors=['.listeUebersicht strong','.listeErgebnis','.listeNotizKopf span']
 def atomic():
  for selector in selectors:
   assert page.locator(selector).evaluate_all('(els)=>els.every(e=>e.childNodes.length===1 && e.firstChild.nodeType===3)'),selector
 atomic()
 title=page.locator('.listeUebersicht strong')
 assert title.inner_text()=='2 Parking lots'
 header=page.locator('.listeKopf').bounding_box()
 actions=page.locator('.listeKopfAktionen').bounding_box()
 sort=page.locator('.listeSortierung').bounding_box()
 assert header['height']<60,header
 assert abs(sort['x']+sort['width']-header['x']-header['width'])<3
 for note in page.locator('.listeNotiz').all():
  n=note.bounding_box();scroll=page.locator('.listeScroll').bounding_box()
  assert n['y']+n['height']<=scroll['y']+scroll['height']+1
 page.screenshot(path=str(out/'liste-screenshot-fall.png'))
 # Mutation: getrennte Textknoten wie im fehlerhaften Titel muessen auffallen.
 title.evaluate('(e)=>{e.textContent="";e.append(document.createTextNode("2 "),document.createTextNode("Parking lots"));}')
 try: atomic()
 except AssertionError: print('Mutation erkannt: geteilter Cohtml-Text.')
 else: raise AssertionError('Mutation blieb unentdeckt')
 print('Screenshot-Fall: eine Headerzeile, rechtsbuendige Aktionen, beide Notes voll sichtbar; atomare Texte bestanden.')
 browser.close()
