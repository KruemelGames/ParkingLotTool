from pathlib import Path
from playwright.sync_api import sync_playwright
import json, math, tempfile
out=Path(tempfile.gettempdir())/'plt-liste-browser'
with sync_playwright() as p:
 browser=p.chromium.launch(headless=True)
 page=browser.new_page(viewport={'width':1920,'height':1080})
 errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto((out/'index.html').as_uri());page.wait_for_selector('.listeKachel')
 page.screenshot(path=str(out/'liste-desktop.png'))
 print('Cards',page.locator('.listeKachel').count(),'Panel',page.locator('.panel').bounding_box())
 print('Overflow',page.evaluate('''()=>Array.from(document.querySelectorAll('.listeKachel,.listeRahmen')).filter(e=>e.scrollWidth>e.clientWidth+2).map(e=>[e.className,e.clientWidth,e.scrollWidth])'''))
 fee=page.locator('.listeGebuehrEingabe').first
 fee.fill('37');fee.press('Enter');page.wait_for_timeout(80)
 assert fee.input_value()=='37'
 assert page.evaluate("window.calls.filter(x=>x.key==='ParkplatzGebuehr').length")==1
 fee.fill('12');fee.press('Escape');assert fee.input_value()=='37'
 assert page.evaluate("window.calls.filter(x=>x.key==='ParkplatzGebuehr').length")==1
 fee.fill('abc');fee.press('Enter');assert fee.input_value()=='37'
 fee.fill('99');fee.press('Enter');page.wait_for_timeout(80);assert fee.input_value()=='50'
 fee.fill('0');fee.press('Tab');page.wait_for_timeout(80);assert fee.input_value()=='0'
 rail=page.locator('.listeGebuehrSchiene').first.bounding_box()
 page.mouse.move(rail['x'],rail['y']+13);page.mouse.down()
 page.mouse.move(math.ceil(rail['x']+rail['width']*.5),rail['y']+13);page.mouse.up()
 page.wait_for_timeout(80);print('Slider',fee.input_value(),rail,page.evaluate('window.calls'));assert fee.input_value()=='25'
 search=page.locator('.listeSuche');search.fill('Hauptzugang')
 assert page.locator('.listeKachel').count()==1
 search.fill('nicht vorhanden');assert page.locator('.listeKachel').count()==0
 page.get_by_role('button',name='Suche und Filter zurücksetzen',exact=True).click()
 page.get_by_role('button',name='Probleme',exact=True).click()
 assert page.locator('.listeKachel').count()==8
 assert page.locator('.listeKachelMangel').count()==8
 page.get_by_role('button',name='Alle',exact=True).click()
 page.get_by_role('button',name='Weitere Parkplätze',exact=True).click()
 assert page.locator('.listeSeiten').inner_text().find('Seite 2 von 4')>=0
 first=page.locator('.listeKachel').nth(0)
 second=page.locator('.listeKachel').nth(1)
 before=second.locator('.listeNotiz').inner_text()
 first.get_by_role('button',name='Nächste Info',exact=True).click()
 assert '2/4' in first.locator('.listeNotizKopf').inner_text()
 assert second.locator('.listeNotiz').inner_text()==before
 first.get_by_role('button',name='Vorige Info',exact=True).click()
 assert '1/4' in first.locator('.listeNotizKopf').inner_text()
 first.get_by_role('button',name='Vorige Info',exact=True).click()
 assert '4/4' in first.locator('.listeNotizKopf').inner_text()
 assert second.locator('.listeNotiz').inner_text()==before
 assert page.locator('.listeKopf .listeSuche').count()==1
 assert page.locator('.listeKopf .listeSortierung').count()==1
 page.get_by_role('button',name='Sortieren nach',exact=True).click()
 page.get_by_role('button',name='Größe',exact=True).click()
 page.wait_for_timeout(100);print('Sort',page.locator('.listeKachel').first.inner_text());assert 'Parkplatz 37' in page.locator('.listeKachel').first.inner_text()
 page.get_by_role('button',name='Infowechsel pausieren',exact=True).click()
 page.mouse.move(0,900)
 assert 'Pausiert' in page.locator('.listeInfosteuerung').inner_text()
 page.get_by_role('button',name='Infowechsel fortsetzen',exact=True).click()
 page.mouse.move(0,900)
 page.wait_for_timeout(80)
 assert '15 s' in page.locator('.listeInfosteuerung').inner_text()
 measurements=[]
 for width,scale in [(1920,1),(1280,1),(900,1),(1280,1.5)]:
  page.set_viewport_size({'width':width,'height':1080})
  page.evaluate('(scale)=>{document.documentElement.style.setProperty("--fontSizeXS",`${14*scale}rem`);document.documentElement.style.setProperty("--fontSizeS",`${16*scale}rem`);document.documentElement.style.setProperty("--fontSizeL",`${20*scale}rem`)}',scale)
  page.wait_for_timeout(80)
  over=page.evaluate('''()=>Array.from(document.querySelectorAll('.listeKachel,.listeRahmen')).filter(e=>e.scrollWidth>e.clientWidth+2).map(e=>[e.className,e.clientWidth,e.scrollWidth])''')
  assert not over,(width,scale,over)
  measurements.append({'width':width,'fontScale':scale,'height':page.locator('.panel').bounding_box()['height']})
  page.screenshot(path=str(out/f'liste-{width}-{scale}.png'))
 assert not errors,errors
 print('UI: Zahleneingabe, Enter, Escape, ungültige Eingabe, Grenzen, Blur, Regler, Suche, Filter, Seiten, Sortierung und Rückwärtsnavigation bestanden.')
 print('Layout:',json.dumps(measurements))
 print('Errors',errors)
 browser.close()
