using System.Text.Json;
using Kalyx.Core.Models;

namespace Kalyx.Core;

public static class HtmlRenderer
{
    public static string Render(string graphJson, string logoDataUri = "")
    {
        return """
<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="description" content="KALYX C# code knowledge graph visualization"><title>KALYX Knowledge Graph</title>
<style>
html,body,#graph{height:100%;margin:0;background:#050817;color:#eee;font-family:system-ui;overflow:hidden}
canvas{width:100%;height:100%;display:block}
#brand{position:fixed;left:14px;top:12px;z-index:5;width:68px;height:42px;padding:4px 6px;display:flex;align-items:center;justify-content:center;background:rgba(5,8,23,.82);border:1px solid rgba(64,181,255,.18);border-radius:8px;box-shadow:0 4px 16px rgba(0,0,0,.2)}
#brand img{display:block;width:56px;height:34px;object-fit:contain}
</style>
</head><body><div id="brand"><img src="LOGO_DATA_URI" alt="KALYX"></div><canvas id="graph"></canvas><script>
const graph = GRAPH_JSON;
const canvas=document.getElementById('graph'),ctx=canvas.getContext('2d');
function resize(){canvas.width=devicePixelRatio*innerWidth;canvas.height=devicePixelRatio*innerHeight;canvas.style.width=innerWidth+'px';canvas.style.height=innerHeight+'px'} resize();addEventListener('resize',resize);
const nodeMap=graph.Nodes||graph.nodes||{}, edges=graph.Edges||graph.edges||[];
const ids=Object.keys(nodeMap), nodes=ids.map(id=>{const raw=nodeMap[id];return{id:raw.Id||raw.id||id,name:raw.Name||raw.name||raw.Label||raw.label||id,community:raw.Community??raw.community??0,x:innerWidth*(.2+.6*Math.random()),y:innerHeight*(.2+.6*Math.random()),vx:0,vy:0}});
const map=new Map(nodes.map(n=>[n.id,n])), colors=['#20b8ff','#725cff','#0b79ff','#ec3fa9','#22c55e','#eab308'];
function draw(){ctx.clearRect(0,0,canvas.width,canvas.height);ctx.save();ctx.scale(devicePixelRatio,devicePixelRatio);
for(const e of edges){const from=e.FromId||e.fromId,a=map.get(from),to=e.ToId||e.toId,b=map.get(to);if(!a||!b)continue;ctx.strokeStyle='#8884';ctx.beginPath();ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);ctx.stroke()}
for(const n of nodes){ctx.fillStyle=colors[n.community%colors.length];ctx.beginPath();ctx.arc(n.x,n.y,5,0,Math.PI*2);ctx.fill();ctx.fillStyle='#ddd';ctx.font='10px sans-serif';ctx.fillText(n.name,n.x+7,n.y+3)}ctx.restore()}
function step(){for(const a of nodes){for(const b of nodes){if(a===b)continue;let dx=a.x-b.x,dy=a.y-b.y,d=Math.max(25,dx*dx+dy*dy);a.vx+=dx/d*80;a.vy+=dy/d*80}}for(const e of edges){let a=map.get(e.FromId||e.fromId),b=map.get(e.ToId||e.toId);if(!a||!b)continue;let dx=b.x-a.x,dy=b.y-a.y;a.vx+=dx*.01;a.vy+=dy*.01;b.vx-=dx*.01;b.vy-=dy*.01}for(const n of nodes){n.x=Math.max(20,Math.min(innerWidth-20,n.x+n.vx));n.y=Math.max(20,Math.min(innerHeight-20,n.y+n.vy));n.vx*=.85;n.vy*=.85}draw();requestAnimationFrame(step)}step();
</script></body></html>
""".Replace("GRAPH_JSON", graphJson).Replace("LOGO_DATA_URI", logoDataUri);
    }
}
