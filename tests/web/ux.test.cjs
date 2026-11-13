// 바닐라 웹 화면의 렌더링·설정 경로·이전 주소 호환을 외부 패키지 없이 검사합니다.
const {readFileSync} = require('node:fs');
const {join} = require('node:path');
const vm = require('node:vm');
const {test} = require('node:test');
const assert = require('node:assert/strict');
const html = readFileSync(join(__dirname, '../../src/Gravity.Server/wwwroot/index.html'), 'utf8');
const source = html.match(/<script>([\s\S]*?)<\/script>/)[1].replace(/\bboot\(\);\s*$/, '');

function app({stored = {}, hash = '', denied = false, fetch, navigator} = {}) {
  const listeners = {}, nodes = new Map();
  const node = () => ({innerHTML: '', textContent: '', hidden: true, dataset: {}, style: {setProperty() {}},
    classList: {toggle() {}, remove() {}, add() {}}, querySelectorAll: () => [], addEventListener() {}});
  const document = {addEventListener: (name, fn) => (listeners[name] ||= []).push(fn),
    querySelector: s => {if (!s.startsWith('#')) return null;if (!nodes.has(s)) nodes.set(s, node()); return nodes.get(s);},
    querySelectorAll: () => [], documentElement: {}, body: node()};
  const storage = {'gravity.lang': 'ko', ...stored}; // 기존 테스트는 한국어 화면을 전제한다
  const location = {hash};
  const context = vm.createContext({document, location, window: {addEventListener() {}, location,
    scrollTo() {}, scrollY: 0, innerWidth: 1200, innerHeight: 900},
    localStorage: {getItem(k) {if (denied) throw Error('denied'); return storage[k] ?? null;},
      setItem(k, v) {if (denied) throw Error('denied'); storage[k] = String(v);}},
    setTimeout: () => 1, clearTimeout() {}, console, URL, Blob, fetch, ...(navigator ? {navigator} : {}),
    requestAnimationFrame() {}, cancelAnimationFrame() {}});
  vm.runInContext(source, context);
  return {run: code => vm.runInContext(code, context), listeners, nodes, storage,
    click(dataset) {const button = {dataset, disabled: false, closest: s => s === 'button' ? button : s === '#tlpop' && (dataset.spawnStyle || dataset.popTab || dataset.popMode || dataset.rst) ? nodes.get('#tlpop') : null};
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
  const a = app({denied: true, navigator: {language: 'ko-KR'}});
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
  a.run("state.compat.lockWithQuestingBots=true");
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
  a.click({act:'copyMapConfirm'});
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

test('원작 프리셋 표는 모든 칸(부착물·탄약 자동 목록 포함)에서 모드 아이템을 보여 주지 않습니다', () => {
  const a=app(),v='000000000000000000000091',m='000000000000000000000092';
  a.run(`serverMeta={};GEAR_CATALOG=[{id:'${v}',ko:'바닐라 조준경',en:'Vanilla scope',section:'parts',slot:'scope',equipmentSlots:[],source:'vanilla',mod:false,autoTier:1,armorClass:0,pen:0,price:100},
    {id:'${m}',ko:'모드 조준경',en:'Mod scope',section:'parts',slot:'scope',equipmentSlots:[],source:'SomeParts',mod:true,autoTier:1,armorClass:0,pen:0,price:100}];rebuildSlots();INSTALLED.SomeParts=true;`);
  assert.equal(a.run(`Object.keys(poolView('pmc',0,'parts','scope')).length`),2);
  a.run(`state.tierData.pmc[0].only=true`);
  assert.deepEqual(JSON.parse(a.run(`JSON.stringify(Object.keys(poolView('pmc',0,'parts','scope')))`)),[v]);
});

test('커스텀 프리셋 삭제: 사용 중이면 기본 프리셋으로 돌아가고, 다음 커스텀 번호는 지운 번호를 다시 쓰지 않습니다', () => {
  const a=app();
  a.run(`serverMeta={};customPresets=[{id:'c1',name:'커스텀 프리셋 1',snap:pickKeys(state,RAID_KEYS)},{id:'c2',name:'커스텀 프리셋 2',snap:pickKeys(state,RAID_KEYS)}];state.preset='c2';confirm=()=>true;`);
  a.click({act:'delPreset',arg:'c2'});
  assert.equal(a.run('customPresets.length'),1);
  assert.equal(a.run('state.preset'),'default');
  a.click({act:'delPreset',arg:'c1'});
  a.run(`customPresets=[{id:'c3',name:'커스텀 프리셋 3',snap:{}}]`);
  assert.equal(a.run('customName(customPresets)'),'커스텀 프리셋 4');
});

test('웹 견적: 시작 분대가 그룹 제한에 막히면 서버처럼 나눈 조각으로 장부를 채운다(5차 검토 9번)', () => {
  const a = app();
  const r = a.run(`(()=>{const c={squad:{s1:0,s2:0,s3:0,s4:0,s5:100},groupCap:{on:true,scope:'raid',over:'split',rules:[{size:2,cmp:'ge',count:0},{size:1,cmp:'eq',count:16}]}};
    const gc=raidGroupCap(c);startLedger(c,16,gc,7);const full=!gc.can(1);
    const d={...c,groupCap:{...c.groupCap,over:'drop'}},gd=raidGroupCap(d);startLedger(d,16,gd,7);return JSON.stringify([full,gd.can(1)]);})()`);
  assert.equal(r, '[true,true]'); // split: 1인 16개를 다 씀 / drop: 버려서 1인 자리가 남음
});


test('티어 편집기는 서버처럼 표 번호 티어를 그대로 쓰고, 표보다 높은 직접 티어·티어 관통 상한을 막힘으로 보입니다(Codex 6차 5·6번)', () => {
  const a=app(),ids=[1,2,3,4].map(i=>'0000000000000000000000a'+i);
  a.run(`serverMeta={};GEAR_CATALOG=${JSON.stringify(ids.map((id,i)=>({id,ko:'총'+i,en:'Gun'+i,section:'weapons',slot:'ar',equipmentSlots:['FirstPrimaryWeapon'],source:'vanilla',mod:false,autoTier:i+1,armorClass:0,pen:0,price:100})))};rebuildSlots();`);
  // 스캐브 T3 표·보스 T1 표의 자동 목록 = 그 표 티어 이하(역할 보정 없음)
  assert.equal(a.run(`Object.keys(poolView('scavs',2,'weapons','ar')).length`),3);
  assert.equal(a.run(`Object.keys(poolView('boss',0,'weapons','ar')).length`),1);
  const it=`findItem('weapons','ar','${ids[0]}')`;
  assert.equal(a.run(`blockOf(${it},'weapons','pmc',0)`),null);
  a.run(`state.gear.itemTier['${ids[0]}']=7`);
  assert.equal(a.run(`blockOf(${it},'weapons','pmc',0).k`),'tier');   // T1 표에 넣어도 직접 T7이면 서버는 뺀다
  assert.equal(a.run(`blockOf(${it},'weapons','pmc',6)`),null);
  // 탄: 이 티어 관통 상한을 넘으면 막힘
  assert.equal(a.run(`(state.gear.tiers[0].pen=20,blockOf({id:'x',src:'vanilla',x:50},'ammo','pmc',0).k)`),'pen');
  assert.equal(a.run(`blockOf({id:'x',src:'vanilla',x:10},'ammo','pmc',0)`),null);
});

test('전용 표 만들기처럼 markEdited 뒤에 표를 고치는 버튼도 커스텀 장비 프리셋 사본에 남습니다(Codex 6차 7번)', () => {
  const a=app();
  a.run(`serverMeta={};var __q=[];setTimeout=(f)=>{__q.push(f);return 1;};customTierPresets=[];state.tierPreset='auto';tRole='usec';tBot='pmc';`);
  a.click({act:'tOwn'});
  a.run('__q.splice(0).forEach(f=>f())');
  assert.equal(a.run('customTierPresets.length'),1);
  assert.equal(a.run(`state.tierPreset===customTierPresets[0].id`),true);
  assert.equal(a.run(`!!customTierPresets[0].data.usec`),true);
});

test('파일 장비 프리셋을 읽는 동안 다른 프리셋을 고르면 늦은 응답이 선택을 덮어쓰지 않습니다(Codex 6차 8번)', async () => {
  let resolve;
  const fetch=()=>new Promise(r=>{resolve=r;});
  const a=app({fetch});
  a.run(`serverMeta={};`);
  a.run(`tierPresetApply('apbs-realistic')`);
  a.run(`tierPresetApply('auto')`);
  resolve({ok:true,json:async()=>({settings:{},tables:{},tiers:null})});
  await new Promise(r=>setImmediate(r));await new Promise(r=>setImmediate(r));
  assert.equal(a.run('state.tierPreset'),'auto');
});

test('슬라이더 ↺ 버튼은 기본값과 다를 때만 보이고 누르면 그 값만 기본값으로 돌립니다(1.0.1)', () => {
  const a=app();
  const html=()=>a.run(`slider('spawn.interval',T('간격','Interval'),'')`);
  assert.doesNotMatch(html(),/data-rst=/);
  a.run(`state.spawn.interval=DEFAULTS.spawn.interval+3;state.spawn.waveMax=DEFAULTS.spawn.waveMax+1;`);
  assert.match(html(),/data-rst="spawn\.interval"/);
  assert.match(a.run(`range('spawn.waveMin','spawn.waveMax',T('웨이브','Waves'),'')`),/data-rst="spawn\.waveMin\|spawn\.waveMax"/);
  a.click({rst:'spawn.interval'});
  assert.equal(a.run('state.spawn.interval===DEFAULTS.spawn.interval'),true);
  assert.equal(a.run('state.spawn.waveMax===DEFAULTS.spawn.waveMax+1'),true);
  assert.doesNotMatch(html(),/data-rst=/);
});

test('로그 원래 무리 한 판 최대 인원 슬라이더가 있고 기본 0입니다(1.0.1)', () => {
  const a=app();
  assert.equal(a.run(`DEFAULTS.specials.rogues.vanillaMax`),0);
});

test('시작 PMC 인원 방식·실제 인원 표시·플레이어 지점 스위치(1.0.1)', () => {
  const a=app();
  assert.match(a.run(`shareHint('bigmap','pmc',50)`),/≈ 8|≈ 8 /);
  assert.match(a.run(`shareHint(null,'pmc',50)`),/~|–/);
  a.run(`state.spawn.startMode='count';state.spawn.startPmcMin=9;state.spawn.startPmcMax=12;`);
  assert.equal(a.run(`startPmcOf(mapById('bigmap'),cfgFor(mapById('bigmap')))`),11);
  const html=a.run(`spawnCommon.render(null,true)`);
  assert.match(html,/data-p="spawn\.startPmcMin"/);
  assert.match(html,/data-p="spawn\.startPlayerOnly"/);
  assert.equal(a.run(`CARD_FIELDS.flow.includes('startPlayerOnly')`),true);
});

test('시작 스캐브 인원 방식·스캐브 레이드 PMC 무리 개수 방식(1.0.2)', () => {
  const a=app();
  assert.equal(a.run(`DEFAULTS.spawn.scav.startMode`),'pct');
  assert.equal(a.run(`DEFAULTS.spawn.scav.remainMode`),'pct');
  let html=a.run(`spawnCommon.render(null,true)`);
  assert.match(html,/data-segp="spawn\.scav\.startMode"/);
  assert.match(html,/data-p="spawn\.scav\.startPct"/);
  assert.match(html,/data-p="spawn\.scav\.playerScavRemain"/);
  a.run(`state.spawn.scav.startMode='count';state.spawn.scav.startMin=4;state.spawn.scav.startMax=7;state.spawn.scav.remainMode='count';`);
  assert.equal(a.run(`startScavOf(mapById('bigmap'),cfgFor(mapById('bigmap')))`),6);
  html=a.run(`spawnCommon.render(null,true)`);
  assert.match(html,/data-p="spawn\.scav\.startMin"/);
  assert.match(html,/data-p="spawn\.scav\.remainMax"/);
  assert.doesNotMatch(html,/data-p="spawn\.scav\.startPct"/);
  for(const f of ['scav.startMode','scav.startMin','scav.startMax','scav.remainMode','scav.remainMin','scav.remainMax'])
    assert.equal(a.run(`CARD_FIELDS.scav.includes('${f}')`),true,f);
});
test('ABPS 감지 잠금이 저장된 스폰 모듈 값을 바꾸지 않고, 왼쪽 위 버전은 서버 판을 따릅니다(1.0.2)', async () => {
  const meta = {version: '9.9.9', detect: {abps: true}, locked: {spawn: true, gear: false}, maps: []};
  const ok = body => ({ok: true, status: 200, json: async () => body});
  const fetch = async url => url === 'api/meta' ? ok(meta) : url === 'api/state' ? ok({state: {modules: {spawn: true, gear: true}}}) : ok(null);
  const a = app({fetch});
  await a.run('boot()');
  assert.equal(a.run('state.modules.spawn'), true);
  assert.equal(a.run('saved.modules.spawn'), true);
  assert.equal(a.run('spawnOn()'), false);
  assert.equal(a.run('estimate(mapById("bigmap"))'), null);
  assert.equal(a.run('u("mock")'), '9.9.9');
  assert.doesNotMatch(a.run("page='settings';P.settings?P.settings.render():''"), /data-p="modules\.spawn"[^>]*checked/);
  a.run('abpsDemo=false');
  assert.equal(a.run('spawnOn()'), true);
  assert.equal(a.run("UI.mock.ko===undefined?u('mock'):u('mock')"), '9.9.9');
});
test('서버 판을 못 받아도 기본 버전 표시는 1.0.4입니다', () => {
  assert.match(html, /mock:T\('1\.0\.4','1\.0\.4'\)/);
});

test('타임라인 시작 인원 팝업도 최소·최대 짝을 검사합니다', () => {
  const a=app();a.run("page='spawn';spawnTab='all';state.spawn.scav.startMode='count';openPop('scav',{clientX:20,clientY:20})");
  const out=a.nodes.get('#tlpop').innerHTML;
  assert.match(out, /data-p="spawn\.scav\.startMin"[^>]*data-pair="spawn\.scav\.startMax"/);
  assert.match(out, /data-p="spawn\.scav\.startMax"[^>]*data-pair="spawn\.scav\.startMin"/);
});
test('역전된 시작 인원 견적도 서버의 최대 올림 보정을 따릅니다',()=>{
  const a=app();a.run("state.spawn.scav.startMode='count';state.spawn.scav.startMin=10;state.spawn.scav.startMax=6;state.spawn.startMode='count';state.spawn.startPmcMin=10;state.spawn.startPmcMax=6");
  assert.equal(a.run("startScavOf(mapById('bigmap'),cfgFor(mapById('bigmap')))"),10);
  assert.equal(a.run("startPmcOf(mapById('bigmap'),cfgFor(mapById('bigmap')))"),10);
});

test('생성된 팝업 입력으로 최소를 올려도 최대를 넘지 않고 카드가 갱신됩니다',()=>{
  const a=app();a.run("page='spawn';spawnTab='all';state.spawn.scav.startMode='count';openPop('scav',{clientX:20,clientY:20})");
  const tag=a.nodes.get('#tlpop').innerHTML.match(/<input[^>]*data-p="spawn\.scav\.startMin"[^>]*>/)[0];
  const dataset=Object.fromEntries([...tag.matchAll(/data-([a-z]+)="([^"]*)"/g)].map(m=>[m[1],m[2].replaceAll('&quot;','"')]));
  a.run(`apply({dataset:${JSON.stringify(dataset)},value:'10',type:'range',min:0,max:40,step:1,style:{setProperty(){}}},true)`);
  assert.equal(a.run('state.spawn.scav.startMin'),6);
  assert.equal(a.run('state.spawn.scav.startMax'),6);
  assert.equal(a.run("startScavOf(mapById('bigmap'),cfgFor(mapById('bigmap')))"),6);
  assert.match(a.nodes.get('#main').innerHTML,/value="6"[^>]*data-p="spawn\.scav\.startMin"/);
});
test('미리 굴리기에서 조건부 후보와 일반 등장을 나누고 웨지 이름을 구별합니다', async()=>{
  const urls=[];
  const a=app({fetch:async url=>{urls.push(url);return {json:async()=>({client:true,rows:[{pmc:1,scav:2,bosses:[],specials:['blackdiv'],conditional:['blackdiv','ISBBossCommander','wedge','killa']}]})}}});
  a.run("serverMeta={version:'1.0.3',detect:{blackDiv:true}};page='dev';devMap='woods'");
  a.click({act:'devSim'});
  await new Promise(setImmediate);
  assert.deepEqual(urls,['api/dev/sim?map=woods&n=10']);
  const row=JSON.parse(a.run('JSON.stringify(simRows[0])'));
  assert.deepEqual(row.sp,['블랙 디비전']);
  assert.deepEqual(row.c,['블랙 디비전','ISBBossCommander','웨지','킬라']);
  const view=a.run('P.dev.render()');
  assert.match(view,/조건부 이벤트 후보/);
  assert.match(view,/서버에 저장된 설정/);
  assert.doesNotMatch(view,/블랙 디비전 · 웨지/);
});

test('미리 굴리기 구형 응답은 조건부 목록 없이도 표시합니다', async()=>{
  const a=app({fetch:async()=>({json:async()=>({client:true,rows:[{pmc:1,scav:2,bosses:[],specials:[]}]})})});
  a.run("serverMeta={version:'1.0.3'};page='dev'");a.click({act:'devSim'});await new Promise(setImmediate);
  assert.deepEqual(JSON.parse(a.run('JSON.stringify(simRows[0].c)')),[]);
  assert.match(a.run('P.dev.render()'),/조건부 이벤트 후보/);
});


test('1.0.4 같은 편집창에서 방식을 반복 변경해도 처음 위치를 유지합니다', () => {
  const a=app();
  a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';openPop('wave',{clientX:140,clientY:120})");
  const box=a.nodes.get('#tlpop'), pos=[box.style.left,box.style.top];
  for (const v of ['infinite','start','waves','infinite','waves']) {
    a.click({spawnStyle:'maps.bigmap.style',v});
    assert.deepEqual([box.style.left,box.style.top],pos);
  }
});
test('1.0.4 PMC와 스캐브는 기본 수정과 자세한 수정을 나누고 탭 이동은 값을 바꾸지 않습니다', () => {
  for(const kind of ['wave','scav']) {
    const a=app();a.run(`page='spawn';spawnTab='woods';mapSel='woods';openPop('${kind}',{clientX:140,clientY:120})`);
    const box=a.nodes.get('#tlpop'),before=a.run('JSON.stringify(state)');
    assert.match(box.innerHTML,/data-pop-tab="basic"/);
    assert.match(box.innerHTML,/data-pop-tab="details"/);
    if(kind==='scav') assert.ok(!box.innerHTML.includes('data-p="maps.woods.scav.perPlayer"'));
    a.click({popTab:'details'});
    assert.match(box.innerHTML,kind==='scav'?/data-p="maps.woods.scav.perPlayer"/:/data-p="maps.woods.firstDelay"/);
    assert.equal(a.run('JSON.stringify(state)'),before);
  }
});
test('1.0.4 여러 맵 전체 복사는 변경·유지 내역을 검토한 뒤에만 적용합니다', () => {
  const a=app();a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';batch=['woods'];state.maps.bigmap.style='start';state.maps.woods.style='waves';state.maps.woods.zonesAlt={Original:{wPmc:20}};");
  const zones=a.run('JSON.stringify(state.maps.woods.zonesAlt)');
  a.click({act:'copyMap'});
  assert.equal(a.run('state.maps.woods.style'),'waves');
  assert.match(a.nodes.get('#main').innerHTML,/data-act="copyMapConfirm"/);
  assert.match(a.nodes.get('#main').innerHTML,/스폰 구역/);
  a.click({act:'copyMapConfirm'});
  assert.equal(a.run('state.maps.woods.style'),'start');
  assert.equal(a.run('JSON.stringify(state.maps.woods.zonesAlt)'),zones);
});
test('1.0.4 일괄 편집은 바꾼 항목과 카드 공통값 고정 효과를 남기고 나머지 카드를 보존합니다', () => {
  const a=app();a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';batch=['woods'];ensureOverride('bigmap','flow');state.spawn.interval=9;state.spawn.waveMax=7;writeSetting('maps.bigmap.interval',12)");
  assert.equal(a.run('cfgFor(mapById("woods")).interval'),12);
  assert.equal(a.run('cfgFor(mapById("woods")).waveMax'),7);
  assert.equal(a.run('cardOn("woods","scav")'),false);
  const out=a.run('batchSummary()');
  assert.match(out,/12/);
  assert.match(out,/9/);
  assert.match(out,/공통 값/);
});

test('1.0.4 팝업의 기본값 버튼은 공통 카드도 이 맵에서 되돌리고 창 값을 갱신합니다',()=>{
 const a=app();a.run("page='spawn';spawnTab='woods';mapSel='woods';state.spawn.interval=12;openPop('wave',{clientX:20,clientY:20})");
 a.click({rst:'maps.woods.interval'});
 assert.equal(a.run('fieldValue("woods","spawn.interval")'),6);
 assert.equal(a.run('state.spawn.interval'),12);
 assert.match(a.nodes.get('#tlpop').innerHTML,/value="6" data-p="maps.woods.interval"/);
});

test('1.0.4 미리보기 자세한 수정은 기존 카드의 전체 설정을 제공합니다',()=>{
 const a=app();a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';openPop('wave',{clientX:20,clientY:20})");a.click({popTab:'details'});
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-p="maps.bigmap.startPlayerOnly"/);
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-p="maps.bigmap.ignoreCap.start"/);
 a.run("openPop('panel|limits',{clientX:20,clientY:20})");
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-p="maps.bigmap.capOn"/);
 a.run("openPop('panel|zones',{clientX:20,clientY:20})");
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-p="maps.bigmap.zones.dorms.wPmc"/);
});
test('1.0.4 보스·특수봇·팩션도 미리보기에서 전체 설정과 꺼진 종류를 편집할 수 있습니다',()=>{
 const a=app();a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';openPop('boss|reshala',{clientX:20,clientY:20})");a.click({popTab:'details'});
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-p="bosses.reshala.followers"/);
 a.run("openPop('panel|factions',{clientX:20,clientY:20})");
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-fac="blackdiv"/);
});

test('1.0.4 맵 도구는 그 맵의 등장 대상만 보이고 공통 설정은 분류를 눌러야 보입니다',()=>{
 const a=app();a.run("page='spawn';spawnTab='factory4_day';mapSel='factory4_day';serverMeta={detect:{ruaf:true,untar:true,blackDiv:false,variantMaps:{}}}");
 const map=a.run('previewTools(mapById("factory4_day"))');
 assert.ok(!map.includes('data-pop="panel|shared"'));assert.ok(!map.includes('data-pop="panel|events"'));assert.ok(!map.includes('data-pop="panel|layout"'));assert.ok(!map.includes('data-pop="panel|factions"'));
 a.run("openPop('panel|bosses',{clientX:20,clientY:20})");
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-boss="tagilla"/);assert.doesNotMatch(a.nodes.get('#tlpop').innerHTML,/data-boss="reshala"/);
 a.run("spawnTab='all'");const common=a.run('previewTools(mapById("bigmap"))');
 assert.match(common,/data-preview-group=/);assert.ok(!common.includes('data-pop="panel|events"'));
 a.click({previewGroup:'events'});assert.match(a.run('previewTools(mapById("bigmap"))'),/data-pop="panel|events"/);
});

test('1.0.4 연구소 팩션 설정은 남기고 다른 맵에서는 웨지·연구소 항목을 숨깁니다',()=>{
 const a=app();a.run("page='spawn';spawnTab='laboratory';mapSel='laboratory';FACMETA={blackdiv:{installed:true},ruaf:{installed:false},untar:{installed:false}}");
 assert.match(a.run('previewTools(mapById("laboratory"))'),/data-pop="panel\|factions"/);
 a.run("openPop('panel|factions',{clientX:20,clientY:20})");assert.match(a.nodes.get('#tlpop').innerHTML,/연구소 · 웨지/);
 a.run("spawnTab='bigmap';mapSel='bigmap';openPop('panel|factions',{clientX:20,clientY:20})");assert.doesNotMatch(a.nodes.get('#tlpop').innerHTML,/연구소 · 웨지/);
});

test('1.0.4 13개 맵의 모든 미리보기 패널이 상태를 바꾸지 않고 해당 맵 경로만 렌더링합니다',()=>{
 const a=app();const ids=JSON.parse(a.run('JSON.stringify(MAPS.map(m=>m.id))'));assert.equal(ids.length,13);
 for(const mid of ids){
  a.run(`page='spawn';spawnTab='${mid}';mapSel='${mid}'`);const before=a.run('JSON.stringify(state)');
  for(const panel of ['style','dist','limits','zones','hotspot','near','layout','bosses','specials','factions']){
   a.run(`openPop('panel|${panel}',{clientX:50,clientY:50})`);const out=a.nodes.get('#tlpop').innerHTML;
   assert.ok(out.length>100,mid+':'+panel);assert.doesNotMatch(out,/value="(?:undefined|NaN)"/,mid+':'+panel);
   const paths=[...out.matchAll(/data-p="maps\.([^.]+)\./g)].map(x=>x[1]);assert.ok(paths.every(id=>id===mid),mid+':'+panel+' target map');
  }
  assert.equal(a.run('JSON.stringify(state)'),before,mid+' read-only render');
 }
});

test('1.0.4 공통 타임라인의 자세한 수정도 맵별 값이 아닌 공통 경로를 사용합니다',()=>{
 for(const kind of ['wave','scav']){
  const a=app();a.run(`page='spawn';spawnTab='all';flowMap='woods';mapSel='woods';openPop('${kind}',{clientX:20,clientY:20})`);
  a.click({popTab:'details'});const out=a.nodes.get('#tlpop').innerHTML;
  assert.match(out,kind==='scav'?/data-p="spawn\.scav\.perPlayer"/:/data-p="spawn\.firstDelay"/);
  assert.doesNotMatch(out,/data-p="maps\.woods\./);
  assert.match(out,/모든 맵 공통/);
 }
});

test('Codex 점검: 자세한 수정 탭의 적용 범위 안내가 항목 종류와 맞습니다',()=>{
 const a=app();a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap';tlEdit=true");
 const ctx=()=>(a.nodes.get('#tlpop').innerHTML.match(/<div class="tp-context">([\s\S]*?)<\/div>/)||[])[1]||'';
 for(const [key,want] of [['boss|reshala',/보스 설정 \(이 보스가 나오는 모든 맵\)/],['fac|ruaf',/팩션 모드 설정/],['spec|snipers',/특수 봇/]]){
  a.run(`openPop('${key}',{clientX:20,clientY:20})`);const basic=ctx();
  a.click({popTab:'details'});const det=ctx();
  assert.match(det,want,key+' details');assert.equal(det,basic,key+' basic/details same scope');
  a.run('closePop()');
 }
});

test('Codex 점검: 미리보기 안내 글의 용어와 위치가 실제 화면과 맞습니다',()=>{
 assert.ok(!html.includes('저장바'),'저장 바 띄어쓰기 통일');
 assert.ok(!html.includes('레이드 전체의 견적'),'견적 → 예상치');
 assert.doesNotMatch(html,/사냥조·연구소 게이트·웨지·다른 모드의 조건부 이벤트는 별도/,'블랙 디비전 안내가 사냥조를 별도 경로라 하면서 사냥조에 적용된다고 함');
 assert.ok(!html.includes('스폰 공통 → 레이드 시간'),'레이드 시간 카드 이름은 "레이드 시간 (공통)"');
 const a=app();a.run("page='spawn';spawnTab='bigmap';mapSel='bigmap'");
 a.run('tlEdit=false');assert.match(a.run('rosterBox(mapById("bigmap"))'),/각 페이지에서 정합니다/);
 a.run('tlEdit=true');assert.match(a.run('rosterBox(mapById("bigmap"))'),/이 맵에서 바로 수정/);
});

test('언어: 저장값을 쓰고, 없으면 브라우저 언어(ko*만 한국어)',()=>{
 assert.equal(app({stored:{'gravity.lang':'en'}}).run('lang'),'en');
 assert.equal(app({stored:{'gravity.lang':undefined}}).run('lang'),'en','저장값·navigator 없음');
 assert.equal(app({stored:{'gravity.lang':undefined},navigator:{language:'ko-KR'}}).run('lang'),'ko');
 assert.equal(app({stored:{'gravity.lang':undefined},navigator:{language:'en-US'}}).run('lang'),'en');
 assert.equal(app({stored:{'gravity.lang':'xx'},navigator:{language:'ko'}}).run('lang'),'ko','이상한 저장값은 무시');
 assert.equal(app({denied:true}).run('lang'),'en','저장소 막힘');
});

test('언어 버튼을 누르면 gravity.lang에 저장',()=>{
 const a=app();a.click({lang:'en'});assert.equal(a.storage['gravity.lang'],'en');assert.equal(a.run('lang'),'en');
 a.click({lang:'ko'});assert.equal(a.storage['gravity.lang'],'ko');
});

test('자동 프리셋 이름은 현재 언어로 보이고 직접 지은 이름은 그대로',()=>{
 const a=app();const sn=n=>a.run(`showName(${JSON.stringify(n)})`);
 a.run("lang='en'");
 assert.equal(sn('커스텀 프리셋 3'),'Custom preset 3');assert.equal(sn('내 레이드 2'),'My raid 2');
 assert.equal(sn('내 장비 1'),'My loadout 1');assert.equal(sn('내 스폰 4'),'My spawns 4');
 assert.equal(sn('기본 사본'),'Default copy','기본 제공 프리셋 사본');assert.equal(sn('본섭 느낌 사본 사본'),'Live-like copy copy');
 assert.equal(sn('Night raid'),'Night raid');assert.equal(sn('야간 레이드 사본'),'야간 레이드 copy');assert.equal(sn('커스텀 프리셋 모음'),'커스텀 프리셋 모음');
 a.run("lang='ko'");
 assert.equal(sn('Custom preset 3'),'커스텀 프리셋 3');assert.equal(sn('My raid copy'),'My raid 사본','번호 없는 앞말은 직접 지은 것, 끝의 사본만 번역');
 assert.equal(sn('My raid 2 copy'),'내 레이드 2 사본');assert.equal(sn('Gravity auto copy'),'Gravity 자동 사본');
 assert.equal(a.run("customName([{name:'Custom preset 3'},{name:'커스텀 프리셋 1'}])"),'커스텀 프리셋 4');
 a.run("customPresets=[{id:'c1',name:'커스텀 프리셋 2'}];customTierPresets=[{id:'t1',name:'내 장비 5',tiers:[]}];lang='en'");
 assert.equal(a.run("presetName('c1')"),'Custom preset 2');assert.equal(a.run("gearPresetName('t1')"),'My loadout 5');
 assert.equal(a.run("JSON.stringify(customPresets[0].name)"),'"커스텀 프리셋 2"','저장값은 그대로');
});

test('보스 맵별 확률: 칸이 있으면 그 값, 비우면 공통/기본',()=>{
 const a=app();
 assert.equal(a.run("JSON.stringify(DEFAULTS.bosses.reshala.mapChance)"),'{}');
 a.run("state.bosses.reshala.mapChance={bigmap:80};state.bossCommon={on:true,chance:35}");
 const ch=m=>a.run(`bossChance(BOSSES.find(b=>b.id==='reshala')${m?`,'${m}'`:''})`);
 assert.equal(ch('bigmap'),80);assert.equal(ch('woods'),35);assert.equal(ch(),35);
 a.run("state.bossCommon.on=false");assert.equal(ch('woods'),a.run('state.bosses.reshala.chance'));
 a.run("page='spawn';spawnTab='all';bossSel='reshala'");const html=a.run('P.bosses.render()');
 assert.match(html,/data-mapchance="reshala\|bigmap"[^>]*value="80"/);
 const fire=(v)=>{for(const fn of a.listeners.change||[])fn({target:{dataset:{mapchance:'reshala|bigmap'},value:v,id:''}});};
 fire('120');assert.equal(a.run('state.bosses.reshala.mapChance.bigmap'),100);
 fire('0');assert.equal(a.run('state.bosses.reshala.mapChance.bigmap'),0,'0은 비움과 다름');
 fire('');assert.equal(a.run("'bigmap' in state.bosses.reshala.mapChance"),false,'비우면 키 삭제');
 a.run("state.bosses.reshala.mapChance={bigmap:55};page='spawn';spawnTab='bigmap';mapSel='bigmap';tlEdit=true;openPop('boss|reshala',{clientX:20,clientY:20})");
 assert.match(a.nodes.get('#tlpop').innerHTML,/data-p="bosses\.reshala\.mapChance\.bigmap"/);
 assert.match(a.run("timeline(mapById('bigmap'),true)"),/레샬라 55%/);
});

test('의상 탭: 모드 단위 체크만, 저장 경로는 mods.clothingOn',()=>{
 const a=app();
 assert.ok(a.run("MOD_TABS.some(t=>t[0]==='clothing')"));
 assert.equal(a.run("'clothing' in DEFAULTS.mods"),false,'옛 부위별 일부 사용 값 없음');assert.equal(a.run("'clothingOn' in DEFAULTS.mods"),false,'키 없음 = 외형 그대로');
 assert.equal(a.run("'clothing' in MODS"),false);
 a.run("serverMeta={};GEAR_CATALOG=[];GEAR_CLOTHING=[{id:'a1',source:'ModA',part:'Body',sides:['usec']},{id:'a2',source:'ModA',part:'Feet',sides:['usec','bear']},{id:'b1',source:'ModB',part:'Body',sides:['savage']}];page='items';modTab='clothing'");
 let out=a.run('modSourceList()');
 assert.equal([...out.matchAll(/data-clothsrc="/g)].length,2,'모드 2개 행');assert.match(out,/ModA[\s\S]*?>2</);
 assert.doesNotMatch(out,/data-clothsrc="a1"|data-it/,'옷 하나하나 고르는 칸 없음');
 assert.match(out,/한 번도 저장하지 않았다면 봇 옷을 바꾸지 않습니다/);
 a.click({clothsrc:'ModA'});assert.equal(a.run('JSON.stringify(state.mods.clothingOn)'),'["ModA"]');
 a.click({clothsrc:'ModA'});assert.equal(a.run('JSON.stringify(state.mods.clothingOn)'),'[]','다시 누르면 빼고, 빈 목록 = 모드 옷 모두 뺌');
 a.click({modall:'on'});assert.equal(a.run('JSON.stringify(state.mods.clothingOn)'),'["ModA","ModB"]');
 a.click({modall:'off'});assert.equal(a.run('JSON.stringify(state.mods.clothingOn)'),'[]');
 assert.doesNotMatch(a.run('P.items.render()'),/data-p="mods\.clothing"/,'옛 부위별 슬라이더 없음');
 a.run("GEAR_CLOTHING=[]");assert.match(a.run('modSourceList()'),/의상 모드가 없습니다/);
});
