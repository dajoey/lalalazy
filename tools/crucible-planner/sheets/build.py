import json, collections, os
D=os.path.dirname(os.path.abspath(__file__))
def L(n): return json.load(open(f'{D}/raw/{n}.json'))
def byid(rows): return {r['row_id']:r['fields'] for r in rows}
def bysub(rows):
    g=collections.defaultdict(dict)
    for r in rows: g[r['row_id']][r.get('subrow_id')]=r['fields']
    return g

XC=byid(L('XBMContent')); XCB=bysub(L('XBMContentBattle')); XBD=bysub(L('XBMBattleDetail'))
BDA=byid(L('XBMBattleDetailAction')); XSE=bysub(L('XBMContentStageEvent')); XSEM=bysub(L('XBMContentStageEventMap'))
XCAMP=bysub(L('XBMContentCamp')); XCRSE=bysub(L('XBMContentRandomStageEvent')); XRSE=bysub(L('XBMRandomStageEvent'))
XAT={k:v['Unknown0'] for k,v in byid(L('XBMActionTarget')).items()}
XAE={k:v['Unknown0'] for k,v in byid(L('XBMActionEffectType')).items()}
XEL={k:v['Name'].strip() for k,v in byid(L('XBMElement')).items()}
XSET={k:v['Unknown0'] for k,v in byid(L('XBMStageEventType')).items()}
XENT=byid(L('XBMEntrance')); QUEST=byid(L('Quest_linked'))
ACT=byid(L('Action_linked')); ATR=byid(L('ActionTransient_linked')); ST=byid(L('Status_linked'))
BN=byid(L('BNpcName_linked')); BR=byid(L('BNpcResist_linked')); CFC=byid(L('ContentFinderCondition_linked'))
TT=byid(L('TerritoryType_linked')); IC=byid(L('InstanceContent_linked')); MAP=L('Map_linked')
ACAT={k:v['Name'] for k,v in byid(L('ActionCategory')).items()}
SB=byid(L('XBMScoreBonus'))

BOARD_NAMES={1:"First Board of the Unbroken",2:"Second Board of the Unbroken",3:"Third Board of the Unbroken",4:"First Master's Board",5:"Second Master's Board"}
STAGE_EVENT_TYPE={1:"Start",2:"Enemy",3:"Elite Enemy",4:"Boss",5:"Shop",6:"Campsite",7:"Treasure",8:"Random"}
STAGE_EVENT_ADDON={1:17540,2:17541,3:17542,4:17543,5:17544,6:17545,7:17546,8:17547}
ATTACK_TYPE={0:None,1:"Slashing",2:"Piercing",3:"Blunt",4:"Shot",5:"Magic",6:"Breath",7:"Sound",8:"Limit Break"}
ASPECT={0:None,1:"Fire",2:"Ice",3:"Wind",4:"Earth",5:"Lightning",6:"Water",7:"Unaspected"}
CAST_TYPE={1:"single-target",2:"circle",10:"donut",11:"cross",12:"line/rect",13:"cone",14:"unknown(14)"}
# [INFERRED] index order of BNpcResist.Unknown0 / XBMPet.InflictsStatus
RESIST_IDX=["Slow","Petrification","Paralysis","Silence/Interrupt","Blind","Poison","Stun","Sleep","Bind","Heavy","Doom"]
STARS=["STR","INT","PHY_R","MAG_R","CON"]  # [INFERRED] Unknown5..9

def status_obj(sid):
    if not sid: return None
    s=ST[sid]
    cat=s['StatusCategory']
    return {"id":sid,"name":s['Name'],"category":{1:"beneficial",2:"detrimental"}.get(cat,cat),
            "CanDispel":s['CanDispel'],"IsPermanent":s['IsPermanent'],"MaxStacks":s['MaxStacks'],
            "Unknown8_dispellable_buff_INFERRED":s['Unknown8'],
            "cleansable":bool(cat==2 and s['CanDispel']),
            "dispellable_INFERRED":bool(cat==1 and s['Unknown8']),
            "description":s['Description']}

def action_obj(bda_row):
    b=BDA[bda_row]; aid=b['Action']
    if not aid: return None
    a=ACT[aid]
    st=status_obj(b['Status'])
    tgt=XAT.get(b['ActionTarget']); eff=XAE.get(b['ActionEffectType'])
    responses=[]
    if a['Unknown15']: responses.append("interrupt")
    if st and st['dispellable_INFERRED']: responses.append("dispel")
    if st and st['cleansable']: responses.append("cleanse")
    if b['ActionEffectType'] in (2,3,4,5,6,7,8,10) and b['ActionTarget']!=6: responses.append("move")
    if b['ActionTarget']==3 and b['ActionEffectType']==1: responses.append("aggro_swap_candidate")
    return {"xbm_battle_detail_action_row":bda_row,"action_id":aid,"name":a['Name'],
            "panel_target":{"raw":b['ActionTarget'],"text":tgt},
            "panel_area":{"raw":b['ActionEffectType'],"text":eff},
            "status":st,
            "cast_s":a['Cast100ms']/10,"action_category":ACAT.get(a['ActionCategory']),
            "cast_type":{"raw":a['CastType'],"shape":CAST_TYPE.get(a['CastType'])},
            "effect_range":a['EffectRange'],"x_axis_modifier":a['XAxisModifier'],
            "attack_type":ATTACK_TYPE.get(a['AttackType'],a['AttackType']),"aspect":ASPECT.get(a['Aspect'],a['Aspect']),
            "omen":a['Omen'],
            "interruptible_INFERRED_Action_Unknown15":a['Unknown15'],
            "suggested_responses":responses}

def enemy_obj(bd_row, sub):
    e=XBD[bd_row][sub]
    rbits=BR[e['Resist']]['Unknown0']
    return {"xbm_battle_detail":f"{bd_row}:{sub}","bnpcname_id":e['Name'],"name":BN[e['Name']]['Singular'],
            "portrait_icon":e['Unknown1'],
            "weakness":{"raw":e['Element'],"text":XEL.get(e['Element']) or None},
            "stars_INFERRED":dict(zip(STARS,[e['Unknown5'],e['Unknown6'],e['Unknown7'],e['Unknown8'],e['Unknown9']])),
            "bnpcresist_row":e['Resist'],"bnpcresist_bits":"".join('1' if x else '0' for x in rbits),
            "vulnerable_to_INFERRED":[RESIST_IDX[i] for i,x in enumerate(rbits) if not x],
            "panel_actions":[x for x in (action_obj(e['Unknown2']) if e['Unknown2'] else None, action_obj(e['Unknown3']) if e['Unknown3'] else None) if x]}

boards=[]; name_index=collections.defaultdict(list)
for bid in range(1,6):
    c=XC[bid]; cfc=CFC[c['ContentFinderCondition']]; tt=TT[cfc['TerritoryType']]; ic=IC[cfc['Content']]
    ent=[ (k,v) for k,v in XENT.items() if v['Unknown1']==bid][0]
    nodes=[]; refs=collections.defaultdict(list)
    for sub,se in sorted(XSE[bid].items()):
        t=se['Unknown0']; idx=se['Unknown2']
        n={"node":sub,"type_raw":t,"type":STAGE_EVENT_TYPE.get(t),"depth":se['Unknown1'],"type_index":idx,"unknown3":se['Unknown3']}
        if t in (2,3,4): n["xbm_content_battle"]=f"{bid}:{idx}"; refs[idx].append(f"node {sub} ({STAGE_EVENT_TYPE[t]})")
        if t==6: n["familiar_recover_count"]=XCAMP[bid][idx]['FamiliarRecoverCount']
        if t==8:
            rse=XCRSE[bid][idx]['RandomStageEvent']
            outs=[]
            for osub,o in sorted(XRSE[rse].items()):
                ot=o['Unknown0']; oi=o['Unknown1']; oo={"type":STAGE_EVENT_TYPE.get(ot),"type_raw":ot,"type_index":oi}
                if ot in (2,3,4): oo["xbm_content_battle"]=f"{bid}:{oi}"; refs[oi].append(f"node {sub} (Random->{STAGE_EVENT_TYPE[ot]})")
                if ot==6: oo["familiar_recover_count"]=XCAMP[bid][oi]['FamiliarRecoverCount']
                outs.append(oo)
            n["xbm_random_stage_event_row"]=rse; n["random_outcomes"]=outs
        nodes.append(n)
    edges=sorted({(m['Unknown3'],m['Unknown4']) for m in XSEM[bid].values() if m['Unknown2']!=1 and (m['Unknown3'] or m['Unknown4'])})
    layout={m['Unknown3']:{"x":m['Unknown0'],"y":m['Unknown1']} for m in XSEM[bid].values() if m['Unknown2']==1}
    for n in nodes: n["map_xy"]=layout.get(n["node"])
    battles=[]
    for bsub,cb in sorted(XCB[bid].items()):
        bd=cb['BattleDetail']
        kinds={r.split('(')[1].rstrip(')') for r in refs[bsub]}
        enemies=[enemy_obj(bd,s) for s in sorted(XBD[bd])]
        for e in enemies: name_index[e['bnpcname_id']].append({"board":bid,"battle":bsub,"xbm_battle_detail":bd,"name":e['name']})
        battles.append({"xbm_content_battle":f"{bid}:{bsub}","xbm_battle_detail_row":bd,
            "role":("Boss" if bsub==0 else ("Elite Enemy" if any('Elite' in k for k in kinds) else "Enemy")),
            "reached_via":refs[bsub],"random_only":all(r.find('Random')>=0 for r in refs[bsub]) and bool(refs[bsub]),
            "enemies":enemies})
    boards.append({"xbm_content_row":bid,"name":BOARD_NAMES[bid],"cfc_id":c['ContentFinderCondition'],"cfc_name":cfc['Name'],
        "cfc_short_code":cfc['ShortCode'],"content_type":cfc['ContentType'],"instance_content":cfc['Content'],
        "instance_content_type":22,"time_limit_min":ic['TimeLimitmin'],
        "territory_type_id":cfc['TerritoryType'],"territory_name":tt['Name'],"territory_bg":tt['Bg'],
        "territory_intended_use":tt['TerritoryIntendedUse'],"place_name_id":tt['PlaceName'],
        "maps":[{"map_id":m['row_id'],"id":m['fields']['Id'],"offset":[m['fields']['OffsetX'],m['fields']['OffsetY']]} for m in MAP if m['fields']['TerritoryType']==cfc['TerritoryType']],
        "class_job_level_sync":cfc['ClassJobLevelSync'],"item_level_sync":cfc['ItemLevelSync'],
        "synk_rank":c['SynkRank'],"recommended_rank_raw":c['RecommendedRank'],"team_size_max":c['TeamSize'],
        "unlock_quest":{"id":cfc['UnlockCriteria'],"name":QUEST[cfc['UnlockCriteria']]['Name']},
        "xbm_entrance_row":ent[0],
        "bonus_points":{SB[i]['Name']:p for i,p in enumerate(c['BonusPoints']) if p},
        "node_count":len(nodes),"nodes":nodes,"edges_from_to":edges,"battles":battles})

enums={
 "XBMActionTarget":{k:v for k,v in XAT.items()},
 "XBMActionEffectType":{k:v for k,v in XAE.items()},
 "XBMElement_weakness":{k:v for k,v in XEL.items()},
 "XBMStageEventType(StageEvent.Unknown0)":{k:{"name":STAGE_EVENT_TYPE.get(k),"addon":STAGE_EVENT_ADDON.get(k),"XBMStageEventType.Unknown0":XSET[k]} for k in XSET},
 "BNpcResist.Unknown0_index_INFERRED":dict(enumerate(RESIST_IDX)),
 "XBMBattleDetail.Unknown5-9_INFERRED":STARS,
 "Action.AttackType":ATTACK_TYPE,"Action.Aspect":ASPECT,"Action.CastType":CAST_TYPE,
}
json.dump({"source":"xivapi v2, game version 7.56 (f5af21155b99a524), schema exdschema@2 rev f3cacf3b","boards":boards},open(f'{D}/crucible_map.json','w'),indent=1,ensure_ascii=False)
json.dump(enums,open(f'{D}/enums.json','w'),indent=1,ensure_ascii=False)
det={"territory_type_ids":{b['territory_type_id']:b['xbm_content_row'] for b in boards},
     "cfc_ids":{b['cfc_id']:b['xbm_content_row'] for b in boards},
     "territory_intended_use":62,"content_type":40,"instance_content_type":22,
     "duty_actions":{"Challenge":46750,"Snarl":46751},
     "bnpcname_to_encounters":{k:v for k,v in sorted(name_index.items())}}
json.dump(det,open(f'{D}/detection.json','w'),indent=1,ensure_ascii=False)

# consistency checks
dups={k:v for k,v in name_index.items() if len(v)>1}
print("BNpcName IDs in >1 battle:",dups)
text=collections.defaultdict(set)
for k,v in name_index.items(): text[v[0]['name'].lower()].add(k)
print("shared name text:",{t:sorted(ids) for t,ids in text.items() if len(ids)>1})
ref_bda=set()
for bd in XBD.values():
    for e in bd.values(): ref_bda|={e['Unknown2'],e['Unknown3']}
print("unreferenced BDA rows:",sorted(set(BDA)-ref_bda-{0}))
inc=[]
for b in boards:
    for bt in b['battles']:
        for e in bt['enemies']:
            for a in e['panel_actions']:
                if a['interruptible_INFERRED_Action_Unknown15'] and 'Silence/Interrupt' not in e['vulnerable_to_INFERRED']:
                    inc.append((e['name'],a['name'],'U15 but resist idx3 immune'))
            if 'Silence/Interrupt' in e['vulnerable_to_INFERRED'] and not any(a['interruptible_INFERRED_Action_Unknown15'] for a in e['panel_actions']):
                inc.append((e['name'],'-','idx3 vulnerable but no U15 action'))
print("interrupt consistency issues:",inc)
for b in boards:
    print(b['xbm_content_row'],b['name'],'nodes',b['node_count'],'battles',len(b['battles']),'random-only',[x['xbm_content_battle'] for x in b['battles'] if x['random_only']], 'unreached',[x['xbm_content_battle'] for x in b['battles'] if not x['reached_via']])
