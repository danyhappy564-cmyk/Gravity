// 바닐라 웹 화면의 렌더링·설정 경로·이전 주소 호환을 외부 패키지 없이 검사합니다.
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const vm = require('node:vm');
const {test} = require('node:test');
const assert = require('node:assert/strict');
const html = readFileSync(join(__dirname, '../../src/Gravity.Server/wwwroot/index.html'), 'utf8');
const source = html.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/\bboot\(\);\s*$/, '');

function app({stored = {}, hash = '', denied = false, fetch} = {}) {
  const listeners = {}, nodes = new Map();
  const node = () => ({innerHTML: '', textContent: '', hidden: true, dataset: {}, style: {setProperty() {}},
    classList: {toggle() {}, remove() {}, add() {}}, querySelectorAll: () => [], addEventListener() {}});
  const document = {addEventListener: (name, fn) => (listeners[name] ||= []).push(fn),
    querySelector: s => {if (!s.startsWith('#')) return null;if (!nodes.has(s)) nodes.set(s, node()); return nodes.get(s);},
    querySelectorAll: () => [], documentElement: {}, body: node()};
  const storage = {...stored};
  const location = {hash};
  const context = vm.createContext({document, location, window: {addEventListener() {}, location,
    scrollTo() {}, scrollY: 0, innerWidth: 1200, innerHeight: 900},
    localStorage: {getItem(k) {if (denied) throw Error('denied'); return storage[k] ?? null;},
      setItem(k, v) {if (denied) throw Error('denied'); storage[k] = String(v);}},
    setTimeout: () => 1, clearTimeout() {}, console, URL, Blob, fetch,
    requestAnimationFrame() {}, cancelAnimationFrame() {}});
  vm.runInContext(source, context);
  return {run: code => vm.runInContext(code, context), listeners, nodes, storage,
    click(dataset) {const button = {dataset, disabled: false, closest: s => s === 'button' ? button : s === '#tlpop' && dataset.spawnStyle ? nodes.get('#tlpop') : null};
      for (const fn of listeners.click || []) fn({target: button, clientX: 20, clientY: 20});}};
}

test('웹 스크립트 문법이 유효합니다', () => new Function(source));
test('메뉴에는 스폰 페이지 하나만 있습니다', () => {
  const a = app();
  assert.equal(a.run("NAV.flatMap(x=>x[1]).filter(x=>['flow','maps','spawn'].includes(x[0])).map(x=>x[0]).join(',')"), 'spawn');
});
test('스폰 공통 탭과 맵 탭이 모든 흐름 설정을 제공합니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='all'");
  const common = a.run('P.spawn.render()');
  assert.match(common, /모든 맵\(공통\)/);
  assert.match(common, /data-p="spawn.interval"/);
  a.run("spawnTab='bigmap';mapSel='bigmap'");
  const map = a.run('P.spawn.render()');
  for (const path of ['maps.bigmap.interval', 'maps.bigmap.firstDelay', 'maps.bigmap.scav.refill', 'maps.bigmap.protect.sec'])
    assert.ok(map.includes(`data-p="${path}"`), path);
  assert.match(map, /data-p="maps.bigmap.ovr.flow"/);
});
test('이전 flow/maps 주소와 마지막 페이지를 통합 페이지로 보냅니다', () => {
  for (const old of ['flow', 'maps']) {
    const a = app({hash: '#' + old});
    a.run('restoreNavigation()');
    assert.equal(a.run('page'), 'spawn');
    assert.equal(a.run('spawnTab'), old === 'flow' ? 'all' : 'bigmap');
    const b = app({stored: {'gravity.page': old}});
    b.run('restoreNavigation()');
    assert.equal(b.run('page'), 'spawn');
  }
});
test('구역 종류에 맞는 PMC/스캐브 막대와 옛 비중을 표시합니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap';state.maps.bigmap.zones.dorms={on:true,w:170}");
  const out = a.run('P.spawn.render()');
  assert.match(out, /data-p="maps.bigmap.zones.dorms.wPmc"/);
  assert.match(out, /data-p="maps.bigmap.zones.dorms.wScav"/);
  assert.ok(!out.includes('data-p="maps.bigmap.zones.crack.wScav"'));
  assert.ok(!out.includes('data-p="maps.bigmap.zones.oldgas.wPmc"'));
  assert.match(out, /170%/);
  assert.match(out, /100% = 이 구역의 보통 비중\(합계가 아님\)/);
});
test('PMC/스캐브/팩션 팝업에 허용된 방식만 제공합니다', () => {
  const a = app();
  for (const kind of ['wave', 'start', 'inf', 'scav', 'fac|ruaf', 'fac|blackdiv']) {
    a.run(`openPop('${kind}',{clientX:20,clientY:20})`);
    const out = a.nodes.get('#tlpop').innerHTML;
    assert.match(out, /data-spawn-style=/);
    if (kind.startsWith('fac|')) assert.match(out, /disabled/);
  }
  a.run("openPop('boss|reshala',{clientX:20,clientY:20})");
  assert.ok(!a.nodes.get('#tlpop').innerHTML.includes('data-spawn-style='));
});
test('스캐브 견적과 타임라인은 PMC 방식과 별개로 바뀝니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap';state.maps.bigmap.style='start'");
  assert.ok(!a.run("timeline(mapById('bigmap'),true)").includes('시작 스캐브만'));
  const waves = a.run("estimate(mapById('bigmap')).smax");
  a.run("state.maps.bigmap.scavStyle='start'");
  assert.match(a.run("timeline(mapById('bigmap'),true)"), /시작 스캐브만/);
  assert.ok(a.run("estimate(mapById('bigmap')).smax") < waves);
  a.run("state.maps.bigmap.scavStyle='infinite'");
  assert.match(a.run("timeline(mapById('bigmap'),true)"), /무한 보충/);
});
test('접힌 적용 안내와 저장소 차단 환경을 지원합니다', () => {
  const a = app({denied: true});
  const out = a.run("liveBanner('spawn')");
  assert.match(out, /<details[^>]*data-apply-info/);
  assert.match(out, /Gravity가 지금 바꾸는 것 \d+가지/);
  assert.match(out, /아직 없음/);
  assert.ok(!out.includes('진단 기록(0.6)'));
  for (const page of ['tiers', 'roles', 'weapons', 'items']) {
    const note = a.run(`liveBanner('${page}')`);
    assert.match(note, /<details[^>]*data-apply-info/);
    assert.match(note, /미적용/);
  }
});
test('모든 화면을 한글·영어로 렌더링합니다', () => {
  const a = app();
  for (const lang of ['ko', 'en']) for (const page of a.run('Object.keys(P)')) {
    a.run(`lang='${lang}';page='${page}'`);
    assert.equal(typeof a.run(`P['${page}'].render()`), 'string');
  }
});
test('방식 버튼 입력이 올바른 맵/팩션에만 저장됩니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='woods';mapSel='woods';openPop('scav',{clientX:20,clientY:20})");
  a.click({spawnStyle: 'maps.woods.scavStyle', v: 'start'});
  assert.equal(a.run('state.maps.woods.scavStyle'), 'start');
  assert.equal(a.run('state.maps.bigmap.scavStyle'), 'waves');
  assert.match(a.nodes.get('#tlpop').innerHTML, /data-v="start" class="on"/);
  a.run("openPop('fac|ruaf',{clientX:20,clientY:20})");
  a.click({spawnStyle: 'factions.ruaf.style', v: 'start'});
  assert.equal(a.run('state.factions.ruaf.style'), 'start');
  assert.equal(a.run('state.factions.ruaf.managed'), true);
  a.click({spawnStyle: 'factions.ruaf.style', v: 'infinite'});
  assert.equal(a.run('state.factions.ruaf.style'), 'start');
});
test('맵 덮어쓰기는 현재 공통 값에서 시작하고 일괄 편집을 유지합니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';batch=['woods'];state.spawn.interval=9");
  a.run("apply({dataset:{p:'maps.bigmap.override',t:'b'},type:'checkbox',checked:true},true)");
  assert.equal(a.run('state.maps.bigmap.interval'), 9);
  assert.equal(a.run('state.maps.woods.interval'), 9);
  a.run("apply({dataset:{p:'maps.bigmap.interval',t:'n'},type:'number',value:'12'},true)");
  assert.equal(a.run('state.maps.bigmap.interval'), 12);
  assert.equal(a.run('state.maps.woods.interval'), 12);
  assert.equal(a.run('state.spawn.interval'), 9);
});
test('팝업의 첫 맵 편집은 덮어쓰기를 켜고 공통 값을 보존합니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='woods';mapSel='woods';state.spawn.waveMin=3");
  assert.equal(a.run("tlField('waveMin','woods')[0]"), 'maps.woods.waveMin');
  assert.equal(a.run("tlField('waveMin','woods')[1]"), 'woods:flow');
  a.run("ensureOverride('woods');set('maps.woods.waveMin',5)");
  assert.equal(a.run('state.spawn.waveMin'), 3);
  assert.equal(a.run('state.maps.woods.waveMin'), 5);
});
test('새 구역 막대 저장은 반대 종류의 옛 비중을 바꾸지 않습니다', () => {
  const a = app();
  a.run("state.maps.bigmap.zones.dorms={on:true,w:160};apply({dataset:{p:'maps.bigmap.zones.dorms.wPmc',t:'n'},type:'number',value:'30'},false)");
  assert.equal(a.run('state.maps.bigmap.zones.dorms.wPmc'), 30);
  assert.equal(a.run('state.maps.bigmap.zones.dorms.w'), 160);
  a.run("page='spawn';spawnTab='bigmap'");
  assert.match(a.run('P.spawn.render()'), /value="160" data-p="maps.bigmap.zones.dorms.wScav"/);
});
test('안내 펼침 상태와 새 맵 주소를 저장하고 복구합니다', () => {
  const a = app();
  for (const fn of a.listeners.toggle) fn({target: {dataset: {applyInfo: 'spawn'}, open: true}});
  assert.equal(a.storage['gravity.applyInfo.spawn'], '1');
  assert.match(a.run("liveBanner('spawn')"), /data-apply-info="spawn" open/);
  a.run("selectPage('spawn','woods');persistNavigation()");
  const b = app({stored: a.storage});
  assert.equal(b.run('page'), 'spawn');
  assert.equal(b.run('spawnTab'), 'woods');
});
test('보충 간격 0과 긴 레이드에서도 타임라인 계산이 끝납니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap';state.spawn.scav.refill=0;SVM.raidAdd=600");
  assert.equal(typeof a.run("timeline(mapById('bigmap'),true)"), 'string');
});
test('팝업에서 방식을 바꾼 뒤 현재 방식의 제어를 보여 줍니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='woods';mapSel='woods';openPop('wave',{clientX:20,clientY:20})");
  a.click({spawnStyle:'maps.woods.style',v:'infinite'});
  assert.match(a.nodes.get('#tlpop').innerHTML, /data-p="maps.woods.inf.delay"/);
  assert.ok(!a.nodes.get('#tlpop').innerHTML.includes('data-p="maps.woods.interval"'));
  a.click({spawnStyle:'maps.woods.style',v:'start'});
  assert.match(a.nodes.get('#tlpop').innerHTML, /data-p="maps.woods.startPmcPct"/);
});
test('옛 맵 프리셋을 적용해도 새 스캐브 기본 방식이 표시됩니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';const old=clone(state.maps.bigmap);delete old.scavStyle;customSpawnPresets=[{id:'old',name:'old',kind:'map',map:'bigmap',data:old}];spAct('apply','old')");
  assert.match(a.run('P.spawn.render()'), /data-segp="maps.bigmap.scavStyle" data-v="waves" class="on"/);
});
test('통합 탭이 모든 맵 카드의 값 범위를 표시합니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap'");
  const rendered=a.run('P.spawn.render()');
  assert.match(rendered,/이 맵 값/);
  assert.match(rendered,/공통 값 사용/);
});
test('나중에 고른 일괄 편집 맵도 실제 덮어쓰기를 켭니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';ensureOverride('bigmap');batch=['woods'];apply({dataset:{p:'maps.bigmap.waveMin',t:'n'},type:'number',value:'4'},true)");
  assert.equal(a.run('state.maps.woods.override'), true);
  assert.equal(a.run('cfgFor(mapById(\'woods\')).waveMin'), 4);
});
test('타임라인 방식 버튼도 고른 맵들에 함께 적용합니다', () => {
  const a = app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';batch=['woods'];openPop('scav',{clientX:20,clientY:20})");
  a.click({spawnStyle:'maps.bigmap.scavStyle',v:'start'});
  assert.equal(a.run('state.maps.woods.scavStyle'), 'start');
});

test('수동 티어와 열화상 목록을 검색 결과에서 추가하고 삭제합니다', () => {
  const a = app(), id = '000000000000000000000001';
  a.run(`itemResults=[{id:'${id}',ko:'설치된 조준경',en:'Installed scope',cat:'scope'}]`);
  assert.match(a.run('P.items.render()'), /아이템 티어 직접 지정/);
  a.click({itemAdd: id, itemList: 'tier'});
  assert.equal(a.run(`state.gear.itemTier['${id}']`), 1);
  a.click({itemAdd: id, itemList: 'thermalAdd'});
  a.click({itemAdd: id, itemList: 'thermalRemove'});
  assert.equal(a.run('state.gear.thermalAdd.length'), 1);
  assert.equal(a.run('state.gear.thermalRemove.length'), 1);
  assert.match(a.run('P.weapons.render()'), /열화상 수동 지정/);
  a.click({itemRemove: id, itemList: 'tier'});
  assert.equal(a.run(`state.gear.itemTier['${id}']`), undefined);
});

test('두 배치의 같은 이름 구역을 독립적으로 편집하고 초기화합니다', () => {
  const a = app();
  a.run(`applyMeta({detect:{variantMaps:{}},maps:[{id:'bigmap',raidMin:40,basePmc:16,baseScav:20,
    pmcZones:['Shared','VariantOnly'],scavZones:['Shared'],hasVariant:true,
    originalPmcZones:['Shared','OriginalOnly'],originalScavZones:['Shared']}],automaticVariants:['bigmap']});
    page='spawn';spawnTab='bigmap';mapSel='bigmap'`);
  assert.match(a.run('P.spawn.render()'), /data-zone-layout="original"/);
  a.click({zoneLayout:'original'});
  const out=a.run('P.spawn.render()');
  assert.match(out,/maps.bigmap.zonesAlt.Shared.wPmc/);
  assert.ok(!out.includes('maps.bigmap.zonesAlt.VariantOnly'));
  a.run(`apply({dataset:{p:'maps.bigmap.zonesAlt.Shared.wPmc',t:'n'},type:'number',value:'20'},false)`);
  assert.equal(a.run('state.maps.bigmap.zonesAlt.Shared.wPmc'),20);
  assert.equal(a.run('state.maps.bigmap.zones.Shared'),undefined);
  a.click({act:'zonesReset'});
  assert.equal(a.run('state.maps.bigmap.zonesAlt.Shared.wPmc'),100);
  assert.match(a.run('P.diag.render()'), /자동 감지된 변형 맵/);
});

test('Questing Bots는 경고가 기본이고 잠금을 선택해도 모듈 선택을 보존합니다', () => {
  const a=app();
  a.run("applyMeta({detect:{questingBots:true},locked:{spawn:false}})");
  assert.match(a.run("liveBanner('dashboard')"),/봇이 두 배로 나올 수 있습니다/);
  assert.equal(a.run('spawnLocked()'),false);
  a.run("state.compat.lockWithQuestingBots=true;applyConflicts()");
  assert.equal(a.run('spawnLocked()'),true);
  assert.equal(a.run('state.modules.spawn'),true);
});

test('ORBIT 관리 경고와 탈출 방식 세그먼트가 현재 선택을 따릅니다', () => {
  const a=app();
  a.run("serverMeta={orbit:{installed:true,factionTakeover:['ruaf']}};state.factions.ruaf.managed=true");
  assert.match(a.run('P.orbit.render()'),/행동은 ORBIT, 스폰은 Gravity/);
  assert.match(a.run('P.orbit.render()'),/data-segp="orbit.extractRefillMode"/);
  assert.match(a.run('P.orbit.render()'),/data-p="orbit.extractRefillCap"/);
  a.run("normalizeState({orbit:{refillAfterExtract:false}})");
  assert.equal(a.run('state.orbit.extractRefillMode'),'off');
});

test('아이템 검색은 실제 API를 쓰고 늦게 도착한 이전 검색 결과를 버립니다', async () => {
  const calls=[];
  const a=app({fetch:url=>new Promise(resolve=>calls.push({url,resolve}))});
  a.run("serverMeta={};page='items';itemQuery='old &'");
  const first=a.run('fetchManualItems()');
  a.run("itemQuery='new'");
  const second=a.run('fetchManualItems()');
  assert.equal(calls[0].url,'api/items?q=old%20%26&cat=all');
  calls[1].resolve({ok:true,json:async()=>({items:[{id:'000000000000000000000002',ko:'새 조준경',en:'New scope',cat:'scope'}]})});
  await second;
  calls[0].resolve({ok:true,json:async()=>({items:[{id:'000000000000000000000001',ko:'옛 조준경',en:'Old scope',cat:'scope'}]})});
  await first;
  assert.equal(a.run('itemResults[0].ko'),'새 조준경');
  assert.match(a.run("page='weapons';P.weapons.render()"),/새 조준경/);
});

test('티어 자동 선택과 탈출 방식은 옛 서버용 값도 함께 저장합니다', () => {
  const a=app(),id='000000000000000000000001';
  a.run(`state.gear.itemTier['${id}']=7;apply({dataset:{p:'gear.itemTier.${id}',t:'n'},type:'number',value:'0'},true)`);
  assert.equal(a.run(`state.gear.itemTier['${id}']`),undefined);
  a.click({segp:'orbit.extractRefillMode',v:'off'});
  assert.equal(a.run('state.orbit.refillAfterExtract'),false);
  a.click({segp:'orbit.extractRefillMode',v:'all'});
  assert.equal(a.run('state.orbit.refillAfterExtract'),true);
  a.run("apply({dataset:{p:'orbit.extractRefillCap',t:'n'},type:'number',value:'0'},true)");
  assert.equal(a.run('state.orbit.refillAfterExtract'),false);
});

test('맵 전체 복사는 두 배치의 대상 맵 구역을 보존합니다', () => {
  const a=app();
  a.run("mapSel='bigmap';batch=['woods'];state.maps.woods.zonesAlt={Original:{wPmc:20}};state.maps.bigmap.zonesAlt={Other:{wPmc:80}};");
  a.click({act:'copyMap'});
  assert.equal(a.run('state.maps.woods.zonesAlt.Original.wPmc'),20);
  assert.equal(a.run('state.maps.woods.zonesAlt.Other'),undefined);
});

test('카드를 켜면 현재 공통 값을 복사하고 다른 카드와 공통 값은 보존합니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';state.spawn.scav.refill=75;state.spawn.interval=9");
  a.run("apply({dataset:{p:'maps.bigmap.ovr.scav',t:'b'},type:'checkbox',checked:true},true)");
  assert.equal(a.run('state.maps.bigmap.scav.refill'),75);
  assert.equal(a.run('state.maps.bigmap.ovr.scav'),true);
  assert.equal(a.run('cardOn(\'bigmap\',\'flow\')'),false);
  a.run("apply({dataset:{p:'maps.bigmap.scav.refill',t:'n'},type:'number',value:'120'},true)");
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).scav.refill'),120);
  assert.equal(a.run('cfgFor(mapById(\'woods\')).scav.refill'),75);
  assert.equal(a.run('state.spawn.scav.refill'),75);
  a.run("apply({dataset:{p:'maps.bigmap.ovr.scav',t:'b'},type:'checkbox',checked:false},true);state.spawn.scav.refill=90");
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).scav.refill'),90);
  a.run("ensureOverride('bigmap','scav')");
  assert.equal(a.run('state.maps.bigmap.scav.refill'),90);
});

test('공통 카드마다 따로 설정한 맵 수와 이름을 표시합니다', () => {
  const a=app();
  a.run("ensureOverride('bigmap','scav');ensureOverride('woods','scav');ensureOverride('woods','flow');page='spawn';spawnTab='all'");
  const out=a.run('P.spawn.render()');
  assert.match(out,/data-card-maps="scav"[\s\S]*?2개 맵이 따로 설정함/);
  assert.match(out,/data-card-maps="flow"[\s\S]*?1개 맵이 따로 설정함/);
  assert.match(out,/세관/);
  assert.match(out,/삼림/);
});

test('맵 탭의 여섯 카드가 꺼져 있으면 공통 값을 비활성으로 보여 줍니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';state.spawn.scav.refill=75");
  const out=a.run('P.spawn.render()');
  for(const card of ['flow','scav','near','dist','hotspot','limits']) assert.ok(out.includes(`data-p="maps.bigmap.ovr.${card}"`),card);
  assert.match(out,/value="75" data-p="maps.bigmap.scav.refill"[^>]*disabled/);
  assert.ok(!out.includes('data-p="maps.bigmap.override"'));
});

test('스캐브 팝업 첫 입력은 스캐브 카드만 켜고 공통 설정을 보존합니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='woods';mapSel='woods';openPop('scav',{clientX:20,clientY:20})");
  assert.match(a.nodes.get('#tlpop').innerHTML,/data-p="maps.woods.scav.refill"/);
  const el={dataset:{p:'maps.woods.scav.refill',t:'n',ovr:'woods:scav'},type:'number',value:'120'};
  for(const fn of a.listeners.change||[]) fn({target:el});
  assert.equal(a.run('cardOn(\'woods\',\'scav\')'),true);
  assert.equal(a.run('cardOn(\'woods\',\'flow\')'),false);
  assert.equal(a.run('state.maps.woods.scav.refill'),120);
  assert.equal(a.run('state.spawn.scav.refill'),45);
});

test('타임라인 드래그는 해당 카드만 켜고 맵 견적에 반영합니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='woods';mapSel='woods';applyTimeline('protect',undefined,2,40,mapById('woods'))");
  assert.equal(a.run('cardOn(\'woods\',\'dist\')'),true);
  assert.equal(a.run('state.maps.woods.protect.sec'),120);
  assert.equal(a.run('state.spawn.protect.sec'),90);
  assert.equal(a.run('cardOn(\'woods\',\'scav\')'),false);
  a.run("applyTimeline('scavEnd',undefined,20,40,mapById('woods'))");
  assert.equal(a.run('state.maps.woods.hardStop.scavPct'),50);
  assert.equal(a.run('cfgFor(mapById(\'woods\')).hardStop.scavPct'),50);
});

test('옛 override와 강도 키를 불러오고 명시적 카드 꺼짐이 우선합니다', () => {
  const a=app();
  a.run("normalizeState({spawn:{interval:9},maps:{bigmap:{override:true,interval:12,early:2},woods:{override:true,interval:15,ovr:{flow:false}}}})");
  assert.equal(a.run('state.maps.bigmap.ovr.flow'),true);
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).interval'),12);
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).early'),2);
  assert.equal(a.run('cfgFor(mapById(\'woods\')).interval'),9);
});

test('맵별 근처 보충·거리·그룹 제한도 일괄 편집과 견적을 따릅니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';batch=['woods'];apply({dataset:{p:'maps.bigmap.ovr.near',t:'b'},type:'checkbox',checked:true},true);apply({dataset:{p:'maps.bigmap.near.interval',t:'n'},type:'number',value:'500'},true)");
  assert.equal(a.run('state.maps.woods.near.interval'),500);
  assert.equal(a.run('cfgFor(mapById(\'woods\')).near.interval'),500);
  assert.equal(a.run('state.near.interval'),180);
});

test('맵별 그룹 규칙 추가·삭제·막대·세그먼트는 같은 카드에 저장됩니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';batch=['woods'];ensureOverride('bigmap','limits');ensureOverride('woods','limits')");
  a.click({segp:'maps.bigmap.groupCap.on',v:'true'});
  a.click({gcadd:'1',gcMid:'bigmap'});
  assert.equal(a.run('state.maps.bigmap.groupCap.rules.length'),3);
  assert.equal(a.run('state.maps.woods.groupCap.rules.length'),3);
  a.click({gcdel:'0',gcMid:'bigmap'});
  assert.equal(a.run('state.maps.bigmap.groupCap.rules.length'),2);
  assert.equal(a.run('state.spawn.groupCap.on'),false);
  assert.match(a.run('P.spawn.render()'),/data-p="maps.bigmap.groupCap.rules.0.size"/);
  a.run("apply({dataset:{p:'maps.bigmap.groupCap.rules.0.count',t:'n'},type:'number',value:'0'},true)");
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).groupCap.rules[0].count'),0);
  assert.equal(a.run('state.maps.woods.groupCap.rules[0].count'),0);
});

test('맵별 거리·시작 스캐브·프리셋은 이전 설정을 이어받습니다', () => {
  const a=app();
  a.run("normalizeState({spawn:{protect:{sec:140}},maps:{bigmap:{override:true,interval:12,early:2,pmcDist:55,start:{scav:false}}}});page='spawn';spawnTab='bigmap';mapSel='bigmap'");
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).pmcDist'),55);
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).protect.sec'),140);
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).scav.startOn'),false);
  a.run("customSpawnPresets=[{id:'legacy',name:'legacy',kind:'map',map:'bigmap',data:{override:true,interval:13,early:2.5}}];spAct('apply','legacy')");
  assert.equal(a.run('cardOn(\'bigmap\',\'flow\')'),true);
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).early'),2.5);
  assert.equal(a.run('cfgFor(mapById(\'bigmap\')).interval'),13);
});

test('일괄 편집 중 구역 값은 현재 맵에만 저장됩니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';batch=['woods'];apply({dataset:{p:'maps.bigmap.zones.dorms.wPmc',t:'n'},type:'number',value:'30'},true)");
  assert.equal(a.run('state.maps.woods.zones.dorms'),undefined);
});

test('한글·영어로 모든 카드가 켜진 맵을 그려도 빈 값이 없습니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';Object.keys(CARD_FIELDS).forEach(k=>ensureOverride('bigmap',k));state.maps.bigmap.near.on=true;state.maps.bigmap.groupCap.on=true");
  for(const lang of ['ko','en']){
    a.run(`lang='${lang}'`);
    const out=a.run('P.spawn.render()');
    assert.ok(!out.includes('value="undefined"'));
    assert.ok(!out.includes('NaN'));
  }
});

test('방식 팝업 버튼도 해당 카드만 켜고 나머지 카드는 공통으로 둡니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='woods';mapSel='woods';openPop('scav',{clientX:20,clientY:20})");
  a.click({spawnStyle:'maps.woods.scavStyle',v:'start'});
  assert.equal(a.run('cardOn(\'woods\',\'scav\')'),true);
  assert.equal(a.run('cardOn(\'woods\',\'flow\')'),false);
  assert.equal(a.run('state.maps.woods.scav.refill'),45);
  a.run("openPop('wave',{clientX:20,clientY:20})");
  a.click({spawnStyle:'maps.woods.style',v:'start'});
  assert.equal(a.run('cardOn(\'woods\',\'flow\')'),true);
  assert.equal(a.run('cardOn(\'woods\',\'near\')'),false);
});

test('시작 스캐브 비율은 해당 맵 견적에만 반영됩니다', () => {
  const a=app();
  a.run("state.maps.bigmap.scavStyle='start';ensureOverride('bigmap','scav');state.maps.bigmap.scav.startPct=50");
  assert.equal(a.run('estimate(mapById(\'bigmap\')).smax'),Math.round(a.run("mapById('bigmap').scav")*.5*1.15));
  a.run("state.maps.bigmap.scav.startOn=false");
  assert.equal(a.run('estimate(mapById(\'bigmap\')).smax'),0);
  assert.equal(a.run('state.spawn.scav.startPct'),20);
});

test('스캐브를 보충할 수 없는 설정이면 예상 스캐브가 0입니다', () => {
  const a=app();
  const smax=()=>a.run("estimate(mapById('bigmap')).smax");
  a.run("ensureOverride('bigmap','scav');state.maps.bigmap.scav.startOn=false;state.maps.bigmap.scav.softCap=0");
  assert.equal(smax(),0);
  a.run("state.maps.bigmap.scav.softCap=18;state.maps.bigmap.scav.perPlayer=0");
  assert.equal(smax(),0);
  a.run("state.maps.bigmap.scav.perPlayer=20");
  assert.ok(smax()>0);
  // PMC 무한·스캐브 웨이브에서 PMC 보충 비율 100%: PMC 차례가 열리는 구간(첫 보충~PMC 중단)에는 스캐브 차례가 없다
  a.run("state.maps.bigmap.style='infinite';state.maps.bigmap.scavStyle='waves';state.maps.bigmap.inf.pmcShare=100");
  a.run("ensureOverride('bigmap','flow');state.maps.bigmap.firstDelay=0;state.maps.bigmap.hardStop={...(state.maps.bigmap.hardStop||{}),pmcPct:5,scavPct:5}");
  assert.equal(a.run("cfgFor(mapById('bigmap')).firstDelay"),0);
  assert.equal(smax(),0);
  a.run("state.maps.bigmap.inf.pmcShare=50");
  const half=smax();
  a.run("state.maps.bigmap.inf.batch=state.maps.bigmap.inf.batch*2");
  assert.ok(smax()>half);
});

test('맵 카드의 소프트 캡·플레이어당 한도는 그 맵 견적만 바꿉니다', () => {
  const a=app();
  const other=a.run("estimate(mapById('woods')).smax");
  a.run("ensureOverride('bigmap','scav');state.maps.bigmap.scav.softCap=0");
  assert.equal(a.run("estimate(mapById('bigmap')).smax"),Math.round(a.run("mapById('bigmap').scav")*a.run("state.spawn.scav.startPct")/100*1.15));
  assert.equal(a.run("estimate(mapById('woods')).smax"),other);
  assert.equal(a.run('state.spawn.scav.softCap'),18);
});

test('PMC 첫 보충 전과 PMC 중단 뒤에는 PMC 비율 100%여도 스캐브가 나옵니다', () => {
  const a=app();
  const smax=()=>a.run("estimate(mapById('bigmap')).smax");
  a.run("ensureOverride('bigmap','scav');state.maps.bigmap.scav.startOn=false;state.maps.bigmap.scav.separateStart=false");
  a.run("state.maps.bigmap.style='infinite';state.maps.bigmap.scavStyle='waves';state.maps.bigmap.inf.pmcShare=100");
  a.run("ensureOverride('bigmap','flow');state.maps.bigmap.firstDelay=600");
  assert.equal(a.run("cfgFor(mapById('bigmap')).firstDelay"),600);
  const early=smax();
  assert.ok(early>0);
  // PMC 중단이 스캐브 중단보다 빠르면 그 뒤 구간도 스캐브 차례
  a.run("state.maps.bigmap.hardStop={...(state.maps.bigmap.hardStop||{}),pmcPct:60,scavPct:5}");
  assert.ok(smax()>early);
  // PMC 보충 목록 소진: 60분·간격 10초·PMC 100%·첫 보충 0초·중단 0%·활동 60분/휴식 0분 → 목록 200개를 쓴 뒤는 스캐브
  a.run("state.maps.bigmap.firstDelay=0;state.maps.bigmap.inf.delay=10;state.maps.bigmap.hardStop={...(state.maps.bigmap.hardStop||{}),pmcPct:0,scavPct:0}");
  a.run("state.maps.bigmap.scav.activeMin=60;state.maps.bigmap.scav.activeMax=60;state.maps.bigmap.scav.quietMin=0;state.maps.bigmap.scav.quietMax=0");
  if(a.run("raidOf(mapById('bigmap'))")*60/10>200) assert.ok(smax()>0);
  // 목록이 남는 짧은 간격이 아니면(간격 45초, 레이드 동안 목록이 다 안 떨어짐) PMC 구간 스캐브는 0
  a.run("state.maps.bigmap.inf.delay=45");
  assert.equal(smax(),0);
  // 미궁 PMC가 꺼져 있으면 모든 차례가 스캐브
  a.run("ensureOverride('labyrinth','scav');state.maps.labyrinth.scav.startOn=false;state.maps.labyrinth.style='infinite';state.maps.labyrinth.scavStyle='waves';state.maps.labyrinth.inf.pmcShare=100");
  assert.ok(a.run("estimate(mapById('labyrinth')).smax")>0);
});
test('웹 스캐브 견적은 레이드 전체 그룹 제한의 거절·분할을 따릅니다', () => {
  const a=app();
  const smax=()=>a.run("estimate(mapById('bigmap')).smax");
  a.run("ensureOverride('bigmap','scav');Object.assign(state.maps.bigmap.scav,{startOn:false,separateStart:false,activeMin:60,activeMax:60,quietMin:0,quietMax:0})");
  a.run("state.maps.bigmap.style='infinite';state.maps.bigmap.scavStyle='waves';state.maps.bigmap.inf.pmcShare=100");
  a.run("ensureOverride('bigmap','flow');state.maps.bigmap.firstDelay=0;state.maps.bigmap.hardStop={...(state.maps.bigmap.hardStop||{}),pmcPct:0,scavPct:0}");
  // 1) 간격 45초: 목록이 안 떨어져 PMC 100%면 스캐브 0 → 1인 이상 0개(drop)면 모든 PMC 차례가 스캐브로
  a.run("state.maps.bigmap.inf.delay=45");
  assert.equal(smax(),0);
  a.run("state.spawn.groupCap={on:true,scope:'raid',over:'drop',rules:[{size:1,cmp:'ge',count:0}]}");
  assert.ok(smax()>0);
  // 2) 간격 10초·5인 분대 100%: 제한 없으면 목록(최대 200)이 떨어져 스캐브가 나오지만, 2인 이상 0개(split)면 1인 5개로 나뉘어 목록이 남는다
  a.run("state.spawn.groupCap={on:false,scope:'raid',over:'split',rules:[]};state.spawn.squad={s1:0,s2:0,s3:0,s4:0,s5:100};state.maps.bigmap.inf.delay=10");
  const exhausted=smax();
  if(a.run("raidOf(mapById('bigmap'))")*6>200) assert.ok(exhausted>0);
  a.run("state.spawn.groupCap={on:true,scope:'raid',over:'split',rules:[{size:2,cmp:'ge',count:0}]}");
  assert.equal(smax(),0);
  // alive 범위는 레이드 전체 장부를 쓰지 않는다
  a.run("state.spawn.groupCap={on:true,scope:'alive',over:'drop',rules:[{size:1,cmp:'ge',count:0}]}");
  assert.equal(smax(),exhausted);
});

test('레이더·로그 화면은 원래 스폰 유지와 추가 스폰 확률을 따로 저장합니다', () => {
  const a=app();
  assert.equal(a.run("state.specials.raiders.vanilla"),true);
  assert.equal(a.run("state.specials.raiders.extra.rezervbase"),0);
  assert.equal(a.run("specChance('raiders','rezervbase')"),0);
  assert.equal(a.run("specChance('cultists','woods')"),12);
  a.run("specSel='rogues';page='specials'");
  const html=a.run("P.specials.render()");
  assert.match(html,/specials\.rogues\.vanilla/);
  assert.match(html,/specials\.rogues\.extra\.lighthouse/);
  assert.doesNotMatch(html,/specials\.rogues\.chance\./);
  a.run("specSel='cultists'");
  assert.match(a.run("P.specials.render()"),/specials\.cultists\.chance\.woods/);
  a.run("specSel='raiders';state.specials.raiders.extra.laboratory=30");
  a.click({act:'specZero'});
  assert.equal(a.run("state.specials.raiders.extra.laboratory"),0);
  assert.equal(a.run("state.specials.raiders.chance.laboratory"),65);
});
test('상한 표는 공통·맵별 상한 카드에 있고 옛 팩션 상한 공유를 이어받습니다', () => {
  const a=app();
  assert.equal(a.run("state.spawn.caps.boss.mode"),'separate');
  assert.equal(a.run("state.spawn.caps.pmcReserve"),0);
  assert.equal(a.run("cardFor('spawn.caps.faction.mode')"),'limits');
  a.run("page='spawn';spawnTab='all'");
  const common=a.run('P.spawn.render()');
  for (const p of ['spawn.caps.pmcReserve','spawn.caps.pmcGroups','spawn.caps.boss.max','spawn.caps.event.max'])
    assert.ok(common.includes(`data-p="${p}"`), p);
  assert.match(common,/data-segp="spawn.caps.special.mode" data-v="respect"/);
  a.run("spawnTab='bigmap';mapSel='bigmap';state.maps.bigmap.ovr.limits=true");
  assert.match(a.run('P.spawn.render()'),/data-p="maps.bigmap.caps.pmcReserve"/);
  const b=app();
  b.run("normalizeState({factions:{ruaf:{shareCap:true}}})");
  assert.equal(b.run("state.spawn.caps.faction.mode"),'respect');
  b.run("normalizeState({factions:{ruaf:{shareCap:true}},spawn:{caps:{faction:{mode:'separate'}}}})");
  assert.equal(b.run("state.spawn.caps.faction.mode"),'separate');
  b.run("facSel='ruaf';page='factions'");
  const fac=b.run("P.factions.render()");
  assert.doesNotMatch(fac,/factions\.ruaf\.shareCap/);
  assert.match(fac,/종류별 상한/);
});
test('맵 미리보기 옆 등장 목록은 원본을 켜 두고 보스·팩션을 더하거나 뺍니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap'");
  const r=a.run("mapRoster(mapById('bigmap'))");
  const by=(t,id)=>r.items.find(x=>x.type===t&&x.id===id);
  assert.equal(by('boss','reshala').tag,'van');
  assert.equal(by('boss','reshala').on,true);
  assert.equal(by('spec','cultists').on,true);
  assert.equal(by('spec','raiders'),undefined);
  assert.ok(r.add.some(x=>x.type==='boss'&&x.id==='killa'));
  assert.ok(!r.add.some(x=>x.id==='zryachiy'));
  assert.match(a.run('P.spawn.render()'),/data-roster="boss\|reshala\|bigmap"/);
  a.click({roster:'boss|reshala|bigmap'});
  assert.equal(a.run("state.bosses.reshala.maps.bigmap"),false);
  a.click({roster:'boss|killa|bigmap'});
  assert.equal(a.run("state.bosses.killa.maps.bigmap"),true);
  assert.equal(a.run("mapRoster(mapById('bigmap')).items.find(x=>x.id==='killa').tag"),'add');
  a.click({roster:'spec|cultists|bigmap'});
  assert.equal(a.run("state.specials.cultists.chance.bigmap"),0);
  a.click({roster:'spec|cultists|bigmap'});
  assert.equal(a.run("state.specials.cultists.chance.bigmap"),10);
  a.click({roster:'spec|raiders|rezervbase'});
  assert.equal(a.run("state.specials.raiders.keep.rezervbase"),false);
  assert.equal(a.run("specLane(mapById('rezervbase')).some(x=>x[2]==='raiders')"),false);
  a.click({roster:'fac|ruaf|bigmap'});
  assert.equal(a.run("state.factions.ruaf.managed"),true);
  assert.equal(a.run("state.factions.ruaf.maps.bigmap"),false);
  // 서버 맵 자료(roster)가 있으면 그것으로 원본을 정한다
  a.run("serverMeta={maps:[{id:'woods',roster:{bossKojaniy:35,sectantPriest:12}}]}");
  const w=a.run("mapRoster(mapById('woods'))");
  assert.equal(w.items.find(x=>x.id==='shturman').tag,'van');
  assert.equal(w.items.find(x=>x.id==='goons').tag,'add'); // 웹에서 켜 둔 맵이지만 서버 자료에는 없음
  assert.ok(w.add.some(x=>x.id==='killa'));
  assert.equal(w.items.find(x=>x.id==='bloodhounds'),undefined);
});
test('타임라인에 특수 봇 줄이 따로 있습니다', () => {
  const a=app();
  const svg=a.run("timeline(mapById('bigmap'),true)");
  assert.match(svg,/특수 봇/);
  assert.match(svg,/컬티스트 · 나올 확률 10%/);
  assert.equal(a.run("specLane(mapById('rezervbase')).filter(x=>x[2]==='raiders').map(x=>x[3]+':'+x[4]).join()"),'orig:null,extra:0');
  a.run("state.specials.raiders.extra.rezervbase=30");
  assert.equal(a.run("specLane(mapById('rezervbase')).filter(x=>x[2]==='raiders').map(x=>x[3]+':'+x[4]).join()"),'orig:null,extra:30');
  // 1.0: 끄면 줄이 사라지고, 시각을 끌면 tFrom/tTo가 함께 움직인다
  a.run("state.specials.snipers.on=false");
  assert.ok(!a.run("specLane(mapById('bigmap')).some(x=>x[2]==='snipers')"));
  a.run("state.specials.cultists.tFrom=10;state.specials.cultists.tTo=15");
  assert.equal(a.run("JSON.stringify(dragVals('spec','cultists',20,100,mapById('bigmap')))"),JSON.stringify([['specials.cultists.tFrom',20],['specials.cultists.tTo',25]]));
  a.run("state.bosses.reshala.on=false");
  assert.ok(!/레샬라/.test(a.run("timeline(mapById('bigmap'),true)")));
});
test('1.0 기본 프리셋은 잠기고 값을 바꾸면 커스텀 프리셋 N이 생깁니다', () => {
  const a = app();
  a.run("state.preset='default';customPresets=[];writeSetting('spawn.interval',5)");
  assert.equal(a.run('customPresets.length'), 1);
  assert.equal(a.run('customPresets[0].name'), '커스텀 프리셋 1');
  assert.equal(a.run('state.preset'), a.run('customPresets[0].id'));
  a.run("writeSetting('spawn.interval',6)");
  assert.equal(a.run('customPresets.length'), 1);
  assert.equal(a.run("JSON.stringify(PRESETS.find(p=>p.id==='default').patch)"), '{}');
  a.run("applyPreset('live');writeSetting('spawn.interval',7)");
  assert.equal(a.run('customPresets[1].name'), '커스텀 프리셋 2');
  a.run("customTierPresets=[];state.tierPreset='auto';writeSetting('gear.itemTier.abc',3)");
  assert.equal(a.run('customTierPresets.length'), 1);
  assert.equal(a.run('state.tierPreset'), a.run('customTierPresets[0].id'));
});

test('아이템 티어: 자동 티어 근거를 보여 주고 선택한 아이템을 한꺼번에 지정합니다', () => {
  const a=app(),gun='000000000000000000000011',ammo='000000000000000000000012';
  a.run(`serverMeta={};GEAR_CATALOG=[{id:'${gun}',ko:'시험 소총',en:'Test rifle',section:'weapons',slot:'ar',equipmentSlots:['FirstPrimaryWeapon','SecondPrimaryWeapon'],source:'vanilla',mod:false,autoTier:3,armorClass:0,pen:0,price:50000,tierGroup:'FirstPrimaryWeapon',tierRank:5,tierSize:14},
    {id:'${ammo}',ko:'시험 탄',en:'Test round',section:'ammo',slot:'Caliber556x45NATO',equipmentSlots:[],source:'vanilla',mod:false,autoTier:7,armorClass:0,pen:40,price:900}];rebuildSlots();page='itemtiers';`);
  const html=a.run('P.itemtiers.render()');
  assert.match(html,/주무기 14개를 가격순/);
  assert.match(a.run(`itWhy(GEAR_CATALOG[1])`),/관통 40/);
  a.click({itsel:gun});a.click({itbulk:'5'});
  assert.equal(a.run(`state.gear.itemTier['${gun}']`),5);
  a.click({itset:`${gun}|0`});
  assert.equal(a.run(`state.gear.itemTier['${gun}']`),undefined);
  assert.match(a.run(`tierSummary('pmc',0)`),/주무기/);
  assert.match(a.run(`tierSummary('pmc',0)`),/앞·뒤판 등급/);
  assert.match(a.run(`tierSummary('pmc',0)`),/data-sum="w:prim"/);
});

test('아이템 티어: 꺼진 모드 아이템을 표시하고 의약품 탭과 탄약 가격 근거를 보여 줍니다', () => {
  const a=app(),mod='000000000000000000000021',med='000000000000000000000022',ammo='000000000000000000000023';
  a.run(`serverMeta={};GEAR_CATALOG=[{id:'${mod}',ko:'모드 소총',en:'Mod rifle',section:'weapons',slot:'ar',equipmentSlots:['FirstPrimaryWeapon'],source:'SomeGuns',mod:true,autoTier:4,armorClass:0,pen:0,price:90000,tierGroup:'weapons/ar',tierRank:30,tierSize:50},
    {id:'${med}',ko:'시험 약',en:'Test med',section:'meds',slot:'meds',equipmentSlots:[],source:'vanilla',mod:false,autoTier:2,armorClass:0,pen:0,price:5000,tierGroup:'meds/meds',tierRank:3,tierSize:10},
    {id:'${ammo}',ko:'시험 탄',en:'Test round',section:'ammo',slot:'Caliber556x45NATO',equipmentSlots:[],source:'vanilla',mod:false,autoTier:6,armorClass:0,pen:44,price:900,tierGroup:'ammo/Caliber556x45NATO',tierRank:40,tierSize:45}];
    rebuildSlots();page='itemtiers';state.mods.disabledSources=['SomeGuns'];`);
  assert.match(a.run('P.itemtiers.render()'),/꺼진 모드/);
  assert.match(a.run(`itWhy(GEAR_CATALOG[0])`),/봇 기본 목록 밖의 돌격소총/);
  assert.match(a.run(`itWhy(GEAR_CATALOG[2])`),/구경 556x45NATO 탄 45개/);
  a.click({it:'sec|meds'});
  assert.match(a.run('P.itemtiers.render()'),/시험 약/);
  assert.match(a.run(`itWhy(GEAR_CATALOG[1])`),/의약품 10개/);
});

test('부착물: 티어 이하만 보이고 직접 뺀 것이 우선이며, 티어를 끄면 전부 보입니다', () => {
  const a=app(),cheap='000000000000000000000061',dear='000000000000000000000062';
  a.run(`serverMeta={};GEAR_CATALOG=[{id:'${cheap}',ko:'싼 조준경',en:'Cheap scope',section:'parts',slot:'scope',equipmentSlots:[],source:'vanilla',mod:false,autoTier:1,armorClass:0,pen:0,price:100,tierGroup:'parts/scope',tierRank:1,tierSize:7},
    {id:'${dear}',ko:'비싼 조준경',en:'Dear scope',section:'parts',slot:'scope',equipmentSlots:[],source:'vanilla',mod:false,autoTier:7,armorClass:0,pen:0,price:9000,tierGroup:'parts/scope',tierRank:7,tierSize:7}];rebuildSlots();`);
  assert.deepEqual(JSON.parse(a.run(`JSON.stringify(Object.keys(poolView('pmc',0,'parts','scope')))`)),[cheap]);
  a.run(`state.tierData.pmc[0].parts.scope['${cheap}']=0`);
  assert.equal(a.run(`poolView('pmc',0,'parts','scope')['${cheap}']`),0);
  a.run(`state.weapons.partTiers=false`);
  assert.equal(a.run(`Object.keys(poolView('pmc',0,'parts','scope')).length`),2);
  a.run(`tBot='pmc';tierSel=0;tSec='parts';page='tieredit'`);
  assert.match(a.run('tierEditor()'),/부착물 티어가 꺼져 있습니다/);
  assert.match(a.run(`tierSummary('pmc',0)`),/data-sum="p:scope"/);
  a.run(`itSec='parts';page='itemtiers'`);
  assert.match(a.run('P.itemtiers.render()'),/비싼 조준경/);
});

test('모드 아이템 0개 설정·PMC만 설정은 티어 편집기에서도 막힘으로 보이고, 모드 아이템 기타 탭이 깨지지 않습니다', () => {
  const a=app(),p='000000000000000000000071';
  a.run(`serverMeta={};GEAR_CATALOG=[{id:'${p}',ko:'모드 조준경',en:'Mod scope',section:'parts',slot:'scope',equipmentSlots:[],source:'SomeParts',mod:true,autoTier:1,armorClass:0,pen:0,price:100,tierGroup:'parts/scope',tierRank:1,tierSize:7}];rebuildSlots();INSTALLED.SomeParts=true;`);
  a.run(`state.mods.subset=true;state.mods.attachments=0`);
  assert.equal(a.run(`blockOf(findItem('parts','scope','${p}'),'parts','pmc').k`),'mod');
  a.run(`state.mods.attachments=10;state.mods.pmcOnly=true`);
  assert.equal(a.run(`blockOf(findItem('parts','scope','${p}'),'parts','pmc')`),null);
  assert.equal(a.run(`blockOf(findItem('parts','scope','${p}'),'parts','scavs').k`),'mod');
  a.run(`page='items';modTab='other'`);
  assert.doesNotThrow(()=>a.run('P.items.render()'));
});
