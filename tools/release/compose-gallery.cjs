/* Lay out Workshop artwork from renders of the shipped models.
 * NODE_PATH=<directory containing sharp> node tools/release/compose-gallery.cjs REPO
 * Source meshes/textures are never edited. Final cards are composed as SVG.
 */
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');
const root = path.resolve(process.argv[2] || '.');
const models = path.join(root,'build/workshop-0.10.0/models');
const out = path.join(root,'art/workshop/0.10.0');
const brand = fs.readFileSync(path.join(root,'art/workshop/brand/hart-wordmark-on-light.svg')).toString('base64');
fs.mkdirSync(out,{recursive:true});
const C={paper:'#F4EFDF',ink:'#173D39',muted:'#58716A',orange:'#C84931',line:'#C8CEC0',white:'#FFFCF3',light:'#E4E8DB'};
const esc=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;');
const rect=(x,y,w,h,fill,rx=0)=>`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}" fill="${fill}"/>`;
const line=(x,y,x2,y2,color=C.line)=>`<path d="M${x} ${y}H${x2}" stroke="${color}" stroke-width="2"/>`;
const text=(x,y,s,size=30,color=C.ink,weight=400,family='Avenir Next')=>`<text x="${x}" y="${y}" font-family="${family}, Arial, sans-serif" font-size="${size}" font-weight="${weight}" fill="${color}">${esc(s)}</text>`;
const rows=(x,y,ss,size=28,color=C.muted,step=40)=>ss.map((s,i)=>text(x,y+i*step,s,size,color)).join('');
const label=(x,y,s,color=C.orange)=>text(x,y,s,24,color,700);
const shadow=(x,y,rx,ry)=>`<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="${C.ink}" opacity=".12" filter="url(#blur)"/>`;
function img(name,x,y,w,h){
 const data=fs.readFileSync(path.join(models,name+'.png')).toString('base64');
 return `<image href="data:image/png;base64,${data}" x="${x}" y="${y}" width="${w}" height="${h}" preserveAspectRatio="xMidYMid meet"/>`;
}
// The outlined master includes the brand's clear space; preserve its colours and proportions.
function hart(x,y,w){return `<image href="data:image/svg+xml;base64,${brand}" x="${x}" y="${y}" width="${w}" height="${w*200/545.62591}" preserveAspectRatio="xMidYMid meet"/>`;}
function base(w=1920,h=1080){return `<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><defs><filter id="blur"><feGaussianBlur stdDeviation="14"/></filter></defs>${rect(0,0,w,h,C.paper)}`;}
function head(n,title,sub){return label(72,62,'FOOD TRUCKS / BIG AMBITIONS')+hart(1570,14,220)+text(1848,62,n,24,C.muted,600)+line(72,90,1848,90)+text(72,180,title,68,C.ink,700)+text(72,232,sub,29,C.muted);}
function foot(n){return line(72,1005,1848,1005)+text(72,1048,'0.10.0 · PRE-RELEASE',22,C.orange,700)+text(630,1048,'The mod’s actual models, rendered for this gallery',22,C.muted)+text(1690,1048,n,22,C.muted);}
async function save(name,body,w=1920,h=1080){
 const svg=base(w,h)+body+'</svg>';
 // SVG is retained outside the release art folder because it embeds large source renders.
 fs.writeFileSync(path.join(root,'build/workshop-0.10.0',name+'.svg'),svg);
 await sharp(Buffer.from(svg)).jpeg({quality:94,chromaSubsampling:'4:4:4'}).toFile(path.join(out,name+'.jpg'));
 console.log('GALLERY',name);
}
async function main(){
 let s=rect(0,0,1024,14,C.orange)+label(56,65,'BIG AMBITIONS / COMMUNITY BETA');
 s+=hart(735,100,248);
 s+=text(50,220,'FOOD',170,C.ink,800,'Avenir Next Condensed')+text(50,370,'TRUCKS',170,C.ink,800,'Avenir Next Condensed');
 s+=text(58,421,'Start with a cart. Grow into a fleet.',32,C.muted,500);
 s+=shadow(660,893,260,30)+img('truck',230,450,800,533)+shadow(220,925,145,20)+img('cart',-22,538,420,420);
 s+=rect(0,968,1024,56,C.ink)+text(50,1007,'PRE-RELEASE',25,C.paper,700)+text(797,1007,'0.10.0',25,C.paper,600);
 await save('00-thumbnail',s,1024,1024);

 s=head('01','From your first cart to a fleet.','Build a street-food business at your own pace.');
 const xs=[72,680,1288];
 xs.forEach(x=>{s+=rect(x,280,560,680,C.white,16);});
 s+=label(104,332,'01  START WITH A CART')+shadow(350,709,160,20)+img('cart',114,354,460,415);
 s+=text(104,810,'A focused first business',34,C.ink,700)+rows(104,860,['2 equipment slots · 1 crew position','Push to a pitch, stock up and serve.','Learn which menu suits your spot.'],25,C.muted,37);
 s+=label(712,332,'02  MOVE UP TO A TRUCK')+shadow(956,705,175,22)+img('truck',694,377,532,355);
 s+=text(712,810,'More room to grow',34,C.ink,700)+rows(712,860,['4 equipment slots · up to 2 crew','Drive between pitches and work','the hatch with more stock on board.'],25,C.muted,37);
 s+=label(1320,332,'03  RUN A STAFFED FLEET')+shadow(1565,706,185,26)+img('truck',1320,365,370,247)+img('truck',1420,494,400,267);
 s+=text(1320,810,'Make the week work',34,C.ink,700)+rows(1320,860,['Recruit, assign shifts and plan routes.','Supply your vehicles through','commissaries and warehouse logistics.'],25,C.muted,37);
 await save('01-progression',s+foot('PROGRESSION'));

 s=head('02','Choose what’s on the menu.','19 food and drink options, plus optional Bakery integration.');
 const menus=[
  ['Street classics',['Hot dogs, burgers, kabobs,','fries and pizza.'],['HotDogRoller','PizzaOven']],
  ['Coffee & bakery',['Coffee, tea, croissants,','donuts and cupcakes.'],['CoffeeStation','PastryCase']],
  ['Cold treats & snacks',['Ice cream, slushies,','popcorn and cotton candy.'],['SlushieMachine','PopcornMachine']],
  ['Soda',['Add refreshments with a','drinks fridge.'],['DrinksCooler']],
  ['New street food',['Dumplings, tacos, chopped cheese','and grilled corn.'],['DumplingSteamer','Griddle','CornGrill']],
  ['With Bakery installed',['Bread, buns, chocolate cookies,','latte and americano.'],['PastryCase','CoffeeStation']]
 ];
 menus.forEach(([title,desc,art],i)=>{
  const x=72+(i%3)*608,y=280+Math.floor(i/3)*350;
  s+=rect(x,y,560,322,i===5?C.light:C.white,12)+text(x+28,y+48,title,33,C.ink,700);
  const width=art.length===3?160:190;
  const total=width*art.length;
  art.forEach((a,j)=>{s+=img(a,x+(560-total)/2+j*width,y+60,width,157);});
  s+=rows(x+28,y+257,desc,25,C.muted,34);
 });
 await save('02-menu',s+foot('FOOD OPTIONS'));

 s=head('03','Four new street foods.','Order the food and matching equipment through a commissary.');
 const foods=[['Dumplings','Dumplings','Dumpling Steamer','DumplingSteamer'],['Tacos','Tacos','Griddle','Griddle'],['Chopped cheese','ChoppedCheese','Griddle','Griddle'],['Grilled corn','CornCob','Corn Grill','CornGrill']];
 foods.forEach(([title,food,machine,model],i)=>{
  const x=72+i*448;
  s+=rect(x,285,416,670,C.white,14)+text(x+24,345,title,34,C.ink,700);
  s+=img(food,x+26,374,364,300)+line(x+24,696,x+392,696);
  s+=img(model,x+35,722,150,170)+text(x+205,776,'Served with',21,C.muted)+text(x+205,815,machine,22,C.ink,700);
 });
 await save('03-new-foods',s+foot('NEW FOODS'));

 s=head('04','Stock. Serve. Restock.','Work your own window and keep the business supplied.');
 s+=rect(72,282,1060,681,C.white,16)+shadow(600,837,380,35)+img('truck-hatch',93,315,1018,622);
 const ops=[['WORK THE WINDOW',['Serve the queue at the cart or hatch.','Use fast-forward or a timed work skip.']],['KEEP THE STOCK MOVING',['Haul supplies, order deliveries or set','standing orders. Take and return stock.']],['GET READY FOR TOMORROW',['Return to your rented commissary bay','for cleaning and overnight parking.']],['WATCH YOUR CASH FLOW',['Player and crew takings arrive at midnight.','Keep cash aside for the next restock.']]];
 ops.forEach(([title,desc],i)=>{const y=325+i*164;s+=label(1180,y,title)+rows(1180,y+48,desc,27,C.muted,38);if(i<3)s+=line(1180,y+122,1848,y+122);});
 await save('04-working-day',s+foot('DAILY OPERATIONS'));

 s=head('05','Give your fleet a plan.','Routes, people and places all shape the business.');
 s+=shadow(529,854,360,28)+img('truck',150,318,700,467)+img('cart',66,586,335,335)+img('truck',405,525,660,440);
 const fleet=[['CREW & WEEKLY ROUTES',['Recruit for the Street food fleet.','Set shifts and plan stops for each weekday.']],['FIND YOUR CUSTOMERS',['Location, time, weather and season matter.','Explore premium spots, parks and events.']],['BUILD THE BUSINESS',['Develop regulars and venue partnerships.','Take catering bookings as you grow.']],['KEEP TRACK IN BIZMAN',['Set prices and review income.','Phone to-dos flag vehicles needing attention.']]];
 fleet.forEach(([title,desc],i)=>{const y=325+i*164;s+=label(1130,y,title)+rows(1130,y+48,desc,27,C.muted,38);if(i<3)s+=line(1130,y+122,1848,y+122);});
 await save('05-fleet',s+foot('FLEET & CITY'));
}
main().catch(e=>{console.error(e);process.exitCode=1;});
