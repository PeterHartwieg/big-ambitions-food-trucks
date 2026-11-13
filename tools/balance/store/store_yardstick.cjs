"use strict";
const fs = require("fs");
const path = require("path");
const os = require("os");

const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const payloadFile = args[0];
if (!payloadFile) { console.error("usage: store_yardstick.cjs PAYLOAD.json [--big-copilot DIR] [--json OUT]"); process.exit(2); }
const bc = opt("--big-copilot", path.join(os.homedir(), "big-copilot"));
const OpenStoreModel = require(path.join(bc, "template", "open-store-model.js"));
const rules = JSON.parse(fs.readFileSync(path.join(bc, "ba_store_rules.json"), "utf8"));
const itemPrices = JSON.parse(fs.readFileSync(path.join(bc, "ba_item_prices.json"), "utf8")).items;
const P = JSON.parse(fs.readFileSync(payloadFile, "utf8"));

function factsFor({freshMarket}){
  const F = JSON.parse(JSON.stringify(P.openStore));
  F.game = {...F.game, promo: 0.5, prices: 1.0, wages: 1.0, tax: 10};
  F.own = {};
  F.built = {};
  for(const [item, m] of Object.entries(F.market)){
    const r = rules.products[item];
    if(r) m.cost = r.w;
    if(freshMarket && m.hoods) for(const h of Object.keys(m.hoods)) m.hoods[h] = [0, 0, null, null];
  }
  return F;
}

const price = item => (itemPrices[item] || {}).p || 0;
const COUNTER = 400;
const REGISTER = price("ba:itemname_cashregister");

const STARTERS = {
  "hot dogs + soda": {slug: "ba:businesstype_fastfoodrestaurant", products: ["ba:itemname_hotdog", "ba:itemname_sodacan"],
    equipment: ["ba:itemname_hotdoggrill", "ba:itemname_drinksfridge"], counters: 2},
  "burgers": {slug: "ba:businesstype_fastfoodrestaurant", products: ["ba:itemname_burger"],
    equipment: ["ba:itemname_industrialgrill"], counters: 2},
  "coffee + croissants": {slug: "ba:businesstype_coffeeshop", products: ["ba:itemname_cupofcoffee", "ba:itemname_croissant"],
    equipment: ["ba:itemname_industrialcoffeemachine", "ba:itemname_bakeryshowcase"], counters: 2},
};

function starterFacts(F, starter){
  const G = JSON.parse(JSON.stringify(F));
  const t = G.types[starter.slug];
  t.products = t.products.filter(([p]) => starter.products.includes(p));
  return G;
}

function starterInvestment(starter, b){
  const equipment = starter.equipment.reduce((s, i) => s + price(i), 0);
  const stock = starter.products.reduce((s, p) => s + rules.products[p].w * rules.products[p].bx, 0);
  return {equipment: REGISTER + starter.counters * COUNTER + equipment, stock, deposit: b.deposit || 0, rent: b.rent || 0,
    total: REGISTER + starter.counters * COUNTER + equipment + stock + (b.deposit || 0) + (b.rent || 0)};
}

const OPEN_06_22 = Array.from({length: 7}, () => [[6, 22]]);

function best(api, slug, b, base){
  const camps = api.facts.campaigns || [];
  let top = null;
  for(let mask = 0; mask < 1 << camps.length; mask++){
    let reach = 0, cost = 0;
    camps.forEach(([, p, sqm], i) => { if(mask >> i & 1){ reach += sqm; cost += p; } });
    const m = api.model.model(slug, b, {...base, reach, cost});
    if(m && (!top || m.profit > top.profit)) top = m;
  }
  return top;
}

function make(F){ return {facts: F, model: OpenStoreModel.create({facts: F, company: {businesses: [], day: 0}})}; }

const buildings = P.premises.buildings.filter(b => b.type === "retail" && (b.size === "A" || b.size === "C"))
  .map(b => ({...b, cap: b.cap || (P.premises.caps.retail || {})[b.size] || 0}));

function q(values, p){
  const v = [...values].sort((a, b) => a - b);
  if(!v.length) return NaN;
  const i = (v.length - 1) * p, lo = Math.floor(i), hi = Math.ceil(i);
  return v[lo] + (v[hi] - v[lo]) * (i - lo);
}
const r0 = x => Math.round(x);

const out = {difficulty: "code defaults: promotion 0.5, prices 1.0, wages 1.0, tax 10%", buildings: buildings.length, runs: []};
for(const fresh of [false, true]){
  const F = factsFor({freshMarket: fresh});
  const market = fresh ? "no other sellers" : "save's sellers";
  for(const slug of ["ba:businesstype_fastfoodrestaurant", "ba:businesstype_coffeeshop"]){
    const api = make(F);
    const rows = buildings.map(b => ({b, m: best(api, slug, b, {}), inv: api.model.investment({type: slug}, b)})).filter(r => r.m);
    out.runs.push(summary(`${slug.replace("ba:businesstype_", "")}, full range, 24/7, best marketing`, market, rows,
      r => r.inv ? r.inv.self : NaN));
  }
  for(const [name, s] of Object.entries(STARTERS)){
    const api = make(starterFacts(F, s));
    const rows = buildings.filter(b => b.size === "A").map(b => ({b, m: api.model.model(s.slug, b, {open: OPEN_06_22}), inv: starterInvestment(s, b)}))
      .filter(r => r.m);
    out.runs.push(summary(`starter ${name}, A unit, 06-22, no marketing`, market, rows, r => r.inv.total));
  }
}

function summary(label, market, rows, investment){
  const profit = rows.map(r => r.m.profit);
  const byHood = {};
  rows.forEach(r => (byHood[r.b.hood] = byHood[r.b.hood] || []).push(r.m.profit));
  const med = rows.slice().sort((a, b) => a.m.profit - b.m.profit)[Math.floor(rows.length / 2)];
  return {label, market, n: rows.length,
    profit: {p25: r0(q(profit, .25)), median: r0(q(profit, .5)), p90: r0(q(profit, .9))},
    medianShop: med && {address: med.b.address, hood: med.b.hood, traffic: med.b.traffic, revenue: r0(med.m.revenue), cogs: r0(med.m.cogs),
      wages: r0(med.m.wages), rent: r0(med.m.rent), marketing: r0(med.m.marketing), customers: r0(med.m.customers), profit: r0(med.m.profit)},
    wagesMedian: r0(q(rows.map(r => r.m.wages), .5)),
    investment: {p25: r0(q(rows.map(investment), .25)), median: r0(q(rows.map(investment), .5)), p90: r0(q(rows.map(investment), .9))},
    byHood: Object.fromEntries(Object.entries(byHood).map(([h, v]) => [h.replace("ba:neighborhood_", ""), {n: v.length, median: r0(q(v, .5))}]))};
}

for(const r of out.runs){
  console.log(`\n${r.label} | ${r.market} | ${r.n} buildings`);
  console.log(`  profit/day p25 ${r.profit.p25}  median ${r.profit.median}  p90 ${r.profit.p90}   wages median ${r.wagesMedian}`);
  console.log(`  investment p25 ${r.investment.p25}  median ${r.investment.median}  p90 ${r.investment.p90}`);
  console.log(`  median shop ${JSON.stringify(r.medianShop)}`);
  console.log(`  by neighbourhood ${Object.entries(r.byHood).map(([h, v]) => `${h} ${v.median} (${v.n})`).join(", ")}`);
}
const jsonOut = opt("--json");
if(jsonOut) fs.writeFileSync(jsonOut, JSON.stringify(out, null, 1));
