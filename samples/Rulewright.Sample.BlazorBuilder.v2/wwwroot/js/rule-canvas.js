window.rulewrightFlowBuilder = (function(){
  "use strict";

  /* ============================================================
     Constants & node type definitions
     ============================================================ */
  const NODE_W = 220;
  const HEADER_H = 34;
  const PORT_ROW_H = 26;
  const BODY_EXTRA_H = 46;

  // portLabels: fixed, named input ports (order matters — buildConditionTree/buildRuleJson
  // read specific indices by meaning, not just "however many are wired").
  // dynamicInput + dynamicLabel: AND/OR/NOT groups and Expression nodes grow an extra empty
  // slot as their existing slots fill up (see addConnection/removeConnection).
  const TYPE_DEFS = {
    trigger:   { label:"Fact Input",    badge:"▶",   color:"var(--accent-teal)",   tagPrefix:"TRG", hasInput:false, isAction:false },
    leaf:      { label:"Compare",       badge:"=",   color:"var(--accent-blue)",   tagPrefix:"CMP", hasInput:true,  isAction:false, portLabels:["Field (expr)"] },
    function:  { label:"Custom Function", badge:"ƒ", color:"var(--accent-blue)",   tagPrefix:"FN",  hasInput:false, isAction:false },
    and:       { label:"AND Group",     badge:"∧",   color:"var(--accent-copper)", tagPrefix:"GRP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Input", operator:"AND" },
    or:        { label:"OR Group",      badge:"∨",   color:"var(--accent-copper)", tagPrefix:"GRP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Input", operator:"OR" },
    not:       { label:"NOT Group",     badge:"¬",   color:"var(--accent-copper)", tagPrefix:"GRP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Input", operator:"NOT", maxInputs:1 },
    action:    { label:"Action",        badge:"▣",   color:"var(--accent-green)",  tagPrefix:"ACT", hasInput:true,  isAction:true,  portLabels:["Condition","Value (expr)"] },
    valLiteral:{ label:"Literal",       badge:"\"…\"", color:"var(--accent-purple)", tagPrefix:"LIT", hasInput:false, isAction:false, isValue:true },
    valField:  { label:"Field Ref",     badge:"{f}", color:"var(--accent-purple)", tagPrefix:"FLD", hasInput:false, isAction:false, isValue:true },
    valOp:     { label:"Expression",    badge:"ƒx",  color:"var(--accent-purple)", tagPrefix:"EXP", hasInput:true,  isAction:false, dynamicInput:true, dynamicLabel:"Operand", isValue:true }
  };

  const OPERATORS = [
    "Equals","NotEquals","GreaterThan","GreaterThanOrEqual","LessThan","LessThanOrEqual",
    "Contains","StartsWith","EndsWith","MatchesRegex","In","NotIn","IsNull","IsNotNull"
  ];
  const ACTION_TYPES = ["setOutput","addToOutput","appendToOutput","removeOutput"];
  const EXPR_OPERATORS = ["add","subtract","multiply","divide","modulo","negate","concat","coalesce"];
  const EXPR_ARITY = { negate:{min:1,max:1}, subtract:{min:2,max:2}, divide:{min:2,max:2}, modulo:{min:2,max:2} };

  /* ============================================================
     State
     ============================================================ */
  const state = {
    nodes: new Map(),
    connections: new Map(),
    nextNodeId: 1,
    nextConnId: 1,
    typeCounters: {},
    scale: 1,
    panX: 60,
    panY: 60,
    selectedNode: null,
    selectedConn: null,
    dragNode: null,
    panDrag: null,
    pendingWire: null,
    sampleFact: {
      Customer: { Age: 34, IsVip: true },
      Order: { Total: 120 }
    }
  };

  let dotNetRef = null;
  let canvasWrap, world, wiresSvg, toastEl, resultBanner;

  /* ============================================================
     Utility
     ============================================================ */
  function clamp(v,min,max){ return Math.max(min, Math.min(max, v)); }

  function showToast(msg){
    toastEl.textContent = msg;
    toastEl.classList.add('show');
    clearTimeout(showToast._t);
    showToast._t = setTimeout(()=>toastEl.classList.remove('show'), 3200);
  }

  function screenToWorld(clientX, clientY){
    const rect = canvasWrap.getBoundingClientRect();
    const sx = clientX - rect.left;
    const sy = clientY - rect.top;
    return { x: (sx - state.panX)/state.scale, y: (sy - state.panY)/state.scale };
  }

  function applyWorldTransform(){
    world.style.transform = `translate(${state.panX}px, ${state.panY}px) scale(${state.scale})`;
    document.getElementById('zoomLabel').textContent = Math.round(state.scale*100) + "%";
  }

  function escapeHtml(s){
    return String(s).replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }

  /* ============================================================
     Node model
     ============================================================ */
  function nextTag(type){
    const def = TYPE_DEFS[type];
    const key = def.tagPrefix;
    state.typeCounters[key] = (state.typeCounters[key]||0) + 1;
    return key + "-" + String(state.typeCounters[key]).padStart(2,"0");
  }

  function createNode(type, x, y){
    const def = TYPE_DEFS[type];
    const id = "n" + (state.nextNodeId++);
    let inputCount;
    if(def.portLabels) inputCount = def.portLabels.length;
    else if(def.dynamicInput) inputCount = 1;
    else inputCount = def.hasInput ? 1 : 0;

    const node = {
      id, type, x, y,
      tag: nextTag(type),
      inputs: new Array(inputCount).fill(null),
      config: defaultConfig(type),
      el: null,
      _test: undefined
    };
    state.nodes.set(id, node);
    renderNode(node);
    return node;
  }

  function defaultConfig(type){
    if(type === 'leaf') return { field:"", operator:"GreaterThan", value:"" };
    if(type === 'function') return { name:"", field:"", value:"" };
    if(type === 'action') return { type:"setOutput", branch:"then", target:"", value:"" };
    if(type === 'valLiteral') return { value:"null" };
    if(type === 'valField') return { field:"" };
    if(type === 'valOp') return { operator:"add" };
    return {};
  }

  function nodeHeight(node){
    const def = TYPE_DEFS[node.type];
    if(!def.hasInput) return HEADER_H + BODY_EXTRA_H;
    const isSimpleBody = def.dynamicInput; // groups & expression ops: minimal body text
    const bodyH = isSimpleBody ? 10 : BODY_EXTRA_H - 14;
    return HEADER_H + node.inputs.length * PORT_ROW_H + bodyH;
  }

  function inputPortY(node, idx){
    return HEADER_H + idx*PORT_ROW_H + PORT_ROW_H/2;
  }
  function outputPortY(node){
    return nodeHeight(node)/2;
  }

  /* ============================================================
     Rendering: nodes
     ============================================================ */
  function renderNode(node){
    const def = TYPE_DEFS[node.type];
    let el = node.el;
    if(!el){
      el = document.createElement('div');
      el.className = 'node';
      el.dataset.id = node.id;
      el.style.setProperty('--node-color', def.color);
      world.appendChild(el);
      node.el = el;
      el.addEventListener('mousedown', onNodeMouseDown);
      el.addEventListener('click', (e)=>{ e.stopPropagation(); selectNode(node.id); });
    }
    el.style.transform = `translate(${node.x}px, ${node.y}px)`;
    el.style.height = nodeHeight(node) + 'px';
    el.classList.toggle('selected', state.selectedNode === node.id);
    el.classList.toggle('pass', node._test === true);
    el.classList.toggle('fail', node._test === false);

    let html = `<div class="node-header">
      <div class="node-badge">${def.badge}</div>
      <div class="node-title">${def.label}</div>
      <div class="node-tag">${node.tag}</div>
      <button class="node-del" title="Delete node">×</button>
    </div>`;

    html += `<div class="node-body">${renderNodeBody(node)}</div>`;

    if(def.hasInput){
      html += `<div class="ports-in">`;
      node.inputs.forEach((connId, idx)=>{
        const label = def.portLabels ? (def.portLabels[idx]||`Port ${idx+1}`) : `${def.dynamicLabel||'Input'} ${idx+1}`;
        html += `<div class="port-row" style="height:${PORT_ROW_H}px;">
          <span class="plabel">${escapeHtml(label)}</span>
          <div class="port port-in" data-node="${node.id}" data-idx="${idx}" style="top:${inputPortY(node,idx)}px;"></div>
        </div>`;
      });
      html += `</div>`;
    }
    if(!def.isAction){
      html += `<div class="port port-out" data-node="${node.id}" style="top:${outputPortY(node)}px;"></div>`;
    }

    el.innerHTML = html;
    el.style.setProperty('--node-color', def.color);

    el.querySelector('.node-del').addEventListener('click', (e)=>{
      e.stopPropagation();
      removeNode(node.id);
    });
    if(!def.isAction){
      const outPort = el.querySelector('.port-out');
      outPort.addEventListener('mousedown', (e)=>{ e.stopPropagation(); startWireDrag(node.id, e); });
    }
    el.querySelectorAll('.port-in').forEach(p=>{
      p.addEventListener('mouseup', (e)=>{ e.stopPropagation(); completeWireDrag(node.id, parseInt(p.dataset.idx,10)); });
    });
  }

  function renderNodeBody(node){
    const t = node.type;
    if(t === 'trigger'){
      const paths = collectFactPaths(state.sampleFact);
      return paths.slice(0,4).map(p=>`<span class="pill">${escapeHtml(p)}</span>`).join(' ');
    }
    if(t === 'leaf'){
      const opSym = { GreaterThan:'>',GreaterThanOrEqual:'≥',LessThan:'<',LessThanOrEqual:'≤',Equals:'=',NotEquals:'≠' }[node.config.operator] || node.config.operator;
      const lhs = node.inputs[0] ? '(computed)' : (node.config.field || '(unset)');
      if(['IsNull','IsNotNull'].includes(node.config.operator)){
        return `<span class="summary">${escapeHtml(lhs)} ${escapeHtml(opSym)}</span>`;
      }
      return `<span class="summary">${escapeHtml(lhs)} ${escapeHtml(opSym)} ${escapeHtml(String(node.config.value))}</span>`;
    }
    if(t === 'function'){
      if(!node.config.name) return `<span class="placeholder">Click to configure…</span>`;
      const field = node.config.field ? escapeHtml(node.config.field) : 'fact';
      return `<span class="summary">ƒ ${escapeHtml(node.config.name)}(${field})</span>`;
    }
    if(t === 'action'){
      if(!node.config.target) return `<span class="placeholder">Click to configure…</span>`;
      const branchTag = node.config.branch === 'else' ? 'ELSE' : 'THEN';
      if(node.config.type === 'removeOutput'){
        return `<span class="summary">[${branchTag}] remove ${escapeHtml(node.config.target)}</span>`;
      }
      const valueText = node.inputs[1] ? '(computed)' : String(node.config.value);
      return `<span class="summary">[${branchTag}] ${escapeHtml(node.config.type)} ${escapeHtml(node.config.target)} = ${escapeHtml(valueText)}</span>`;
    }
    if(t === 'valLiteral'){
      return `<span class="summary">${escapeHtml(node.config.value)}</span>`;
    }
    if(t === 'valField'){
      return node.config.field ? `<span class="summary">{${escapeHtml(node.config.field)}}</span>` : `<span class="placeholder">Click to configure…</span>`;
    }
    if(t === 'valOp'){
      return `<span class="summary">${escapeHtml(node.config.operator)}(…)</span>`;
    }
    return `<span class="placeholder" style="font-size:9.5px;">Combines connected inputs</span>`;
  }

  function collectFactPaths(obj, prefix){
    prefix = prefix || "";
    let out = [];
    for(const k in obj){
      const p = prefix ? prefix+"."+k : k;
      if(obj[k] && typeof obj[k] === 'object' && !Array.isArray(obj[k])){
        out = out.concat(collectFactPaths(obj[k], p));
      } else {
        out.push(p);
      }
    }
    return out;
  }

  function removeNode(id){
    const node = state.nodes.get(id);
    if(!node) return;
    [...state.connections.values()].forEach(c=>{
      if(c.from === id || c.to === id) removeConnection(c.id, true);
    });
    node.el.remove();
    state.nodes.delete(id);
    if(state.selectedNode === id){ state.selectedNode = null; renderInspector(); }
    renderWires();
    regenerateJson();
  }

  function clearGraph(){
    state.nodes.forEach(n=>{ if(n.el) n.el.remove(); });
    state.nodes.clear();
    state.connections.clear();
    state.nextNodeId = 1;
    state.nextConnId = 1;
    state.typeCounters = {};
    state.selectedNode = null;
    state.selectedConn = null;
    renderInspector();
  }

  /* ============================================================
     Connections
     ============================================================ */
  function isDescendant(candidateAncestorId, nodeId, seen){
    seen = seen || new Set();
    if(seen.has(nodeId)) return false;
    seen.add(nodeId);
    const node = state.nodes.get(nodeId);
    if(!node) return false;
    for(const connId of node.inputs){
      if(!connId) continue;
      const conn = state.connections.get(connId);
      if(!conn) continue;
      if(conn.from === candidateAncestorId) return true;
      if(isDescendant(candidateAncestorId, conn.from, seen)) return true;
    }
    return false;
  }

  function addConnection(fromId, toId, toIdx){
    if(fromId === toId){ showToast("A node can't connect to itself."); return null; }
    const toNode = state.nodes.get(toId);
    if(!toNode) return null;
    if(toNode.inputs[toIdx]){ showToast("That input is already connected."); return null; }
    if(isDescendant(toId, fromId)){ showToast("That connection would create a loop."); return null; }

    const id = "c" + (state.nextConnId++);
    const conn = { id, from: fromId, to: toId, toIdx };
    state.connections.set(id, conn);
    toNode.inputs[toIdx] = id;

    const def = TYPE_DEFS[toNode.type];
    if(def.dynamicInput && (!def.maxInputs || toNode.inputs.length < def.maxInputs)){
      toNode.inputs.push(null);
    }
    renderNode(toNode);
    renderWires();
    regenerateJson();
    return conn;
  }

  function removeConnection(id, skipRerender){
    const conn = state.connections.get(id);
    if(!conn) return;
    const toNode = state.nodes.get(conn.to);
    if(toNode){
      const idx = toNode.inputs.indexOf(id);
      if(idx > -1) toNode.inputs[idx] = null;
      const def = TYPE_DEFS[toNode.type];
      if(def.dynamicInput){
        while(toNode.inputs.length > 1 &&
              toNode.inputs[toNode.inputs.length-1] === null &&
              toNode.inputs[toNode.inputs.length-2] === null){
          toNode.inputs.pop();
        }
      }
      if(!skipRerender) renderNode(toNode);
    }
    state.connections.delete(id);
    if(state.selectedConn === id) state.selectedConn = null;
    if(!skipRerender){ renderWires(); regenerateJson(); }
  }

  /* ============================================================
     Wire geometry & rendering
     ============================================================ */
  function portWorldPos(nodeId, isOutput, idx){
    const node = state.nodes.get(nodeId);
    if(!node) return {x:0,y:0};
    if(isOutput) return { x: node.x + NODE_W, y: node.y + outputPortY(node) };
    return { x: node.x, y: node.y + inputPortY(node, idx) };
  }

  function orthogonalPath(x1,y1,x2,y2){
    const midX = x2 - x1 > 60 ? (x1+x2)/2 : x1 + 40;
    const midX2 = x2 - x1 > 60 ? (x1+x2)/2 : x2 - 40;
    return { d:`M ${x1} ${y1} L ${midX} ${y1} L ${midX2} ${y2} L ${x2} ${y2}`, bends:[[midX,y1],[midX2,y2]] };
  }

  function renderWires(){
    let svgContent = '';
    state.connections.forEach(conn=>{
      const from = portWorldPos(conn.from, true, 0);
      const to = portWorldPos(conn.to, false, conn.toIdx);
      const path = orthogonalPath(from.x, from.y, to.x, to.y);
      const toNode = state.nodes.get(conn.to);
      const fromNode = state.nodes.get(conn.from);
      let cls = 'wire-path';
      if(state.selectedConn === conn.id) cls += ' selected';
      if(fromNode && fromNode._test === true && toNode && toNode._test !== false) cls += ' pass';
      else if(fromNode && fromNode._test === false) cls += ' fail';

      svgContent += `<path class="${cls}" d="${path.d}" data-conn="${conn.id}"></path>`;
      path.bends.forEach(b=>{
        svgContent += `<circle class="via-dot ${cls.includes('pass')?'pass':cls.includes('fail')?'fail':''}" cx="${b[0]}" cy="${b[1]}" r="3"></circle>`;
      });
      svgContent += `<path d="${path.d}" fill="none" stroke="transparent" stroke-width="14" style="pointer-events:all;cursor:pointer;" data-conn-hit="${conn.id}"></path>`;
      const midB = path.bends[0];
      svgContent += `<g class="wire-del-group" data-conn-del="${conn.id}" style="pointer-events:all;cursor:pointer;">
        <circle class="wire-del" cx="${midB[0]}" cy="${(from.y+to.y)/2}" r="7"></circle>
        <path class="wire-del-x" d="M ${midB[0]-3} ${(from.y+to.y)/2-3} L ${midB[0]+3} ${(from.y+to.y)/2+3} M ${midB[0]+3} ${(from.y+to.y)/2-3} L ${midB[0]-3} ${(from.y+to.y)/2+3}"></path>
      </g>`;
    });

    if(state.pendingWire){
      const from = portWorldPos(state.pendingWire.fromNode, true, 0);
      const path = orthogonalPath(from.x, from.y, state.pendingWire.x, state.pendingWire.y);
      svgContent += `<path class="wire-path hot" stroke-dasharray="5 3" d="${path.d}"></path>`;
    }

    wiresSvg.innerHTML = svgContent;

    let maxX = 400, maxY = 400;
    state.nodes.forEach(n=>{ maxX = Math.max(maxX, n.x + NODE_W + 200); maxY = Math.max(maxY, n.y + nodeHeight(n) + 200); });
    wiresSvg.setAttribute('width', maxX);
    wiresSvg.setAttribute('height', maxY);
    world.style.width = maxX + 'px';
    world.style.height = maxY + 'px';

    wiresSvg.querySelectorAll('[data-conn-hit]').forEach(p=>{
      p.addEventListener('click', (e)=>{ e.stopPropagation(); selectConnection(p.dataset.connHit); });
    });
    wiresSvg.querySelectorAll('[data-conn-del]').forEach(g=>{
      g.addEventListener('click', (e)=>{ e.stopPropagation(); removeConnection(g.dataset.connDel); });
    });
  }

  /* ============================================================
     Selection & Inspector
     ============================================================ */
  function selectNode(id){
    state.selectedNode = id;
    state.selectedConn = null;
    state.nodes.forEach(n=>renderNode(n));
    renderWires();
    renderInspector();
  }
  function selectConnection(id){
    state.selectedConn = id;
    state.selectedNode = null;
    state.nodes.forEach(n=>renderNode(n));
    renderWires();
    renderInspector();
  }
  function clearSelection(){
    state.selectedNode = null;
    state.selectedConn = null;
    state.nodes.forEach(n=>renderNode(n));
    renderWires();
    renderInspector();
  }

  function renderInspector(){
    const body = document.getElementById('nodeInspectorBody');
    if(!state.selectedNode){
      if(state.selectedConn){
        body.innerHTML = `<div class="insp-empty"><span class="insp-empty-icon">⟶</span>Wire selected. Press <strong>Delete</strong> or click the × on the wire to remove it.</div>`;
      } else {
        body.innerHTML = `<div class="insp-empty"><span class="insp-empty-icon">◇</span>Select a node to edit its properties.</div>`;
      }
      return;
    }
    const node = state.nodes.get(state.selectedNode);
    if(!node){ body.innerHTML = ''; return; }
    const def = TYPE_DEFS[node.type];
    let html = `<div style="margin-bottom:12px;"><span class="insp-node-type" style="--node-color:${def.color};color:${def.color}">${node.tag} · ${def.label}</span></div>`;

    if(node.type === 'trigger'){
      html += `<div class="field"><label>Sample fact (used for Test rule)</label>
        <textarea id="factEditor" style="min-height:130px;">${escapeHtml(JSON.stringify(state.sampleFact,null,2))}</textarea></div>
        <div style="font-size:10.5px;color:var(--text-faint);">Paths available: ${collectFactPaths(state.sampleFact).map(p=>'<code>'+p+'</code>').join(', ')}</div>`;
    } else if(node.type === 'leaf'){
      const fieldExprWired = !!node.inputs[0];
      html += `<div class="field"><label>Field path</label>
        <input type="text" id="cfgField" placeholder="Customer.Age" value="${escapeHtml(node.config.field)}" ${fieldExprWired?'disabled':''}></div>`;
      if(fieldExprWired){
        html += `<div style="font-size:10.5px;color:var(--text-faint);margin:-6px 0 11px;">Using the wired <strong>Field (expr)</strong> computed value instead — disconnect that pin to type a plain field path.</div>`;
      }
      html += `<div class="field"><label>Operator</label><select id="cfgOperator">${OPERATORS.map(o=>`<option value="${o}" ${o===node.config.operator?'selected':''}>${o}</option>`).join('')}</select></div>
        <div class="field" id="valueFieldWrap" style="${['IsNull','IsNotNull'].includes(node.config.operator)?'display:none;':''}"><label>Value (constant)</label><input type="text" id="cfgValue" placeholder='18 or true or ["a","b"]' value="${escapeHtml(node.config.value)}"></div>
        <div style="font-size:10.5px;color:var(--text-faint);">A condition's comparison value must be a constant — only its left-hand side can be a computed expression (wire into the Field (expr) pin).</div>`;
    } else if(node.type === 'function'){
      html += `<div class="field"><label>Function name</label><input type="text" id="cfgName" placeholder="IsBusinessDay" value="${escapeHtml(node.config.name)}"></div>
        <div class="field"><label>Field path (optional)</label><input type="text" id="cfgField" placeholder="Customer.Email" value="${escapeHtml(node.config.field)}"></div>
        <div class="field"><label>Value (optional, constant)</label><input type="text" id="cfgValue" placeholder="[10, 20]" value="${escapeHtml(node.config.value)}"></div>
        <div style="font-size:10.5px;color:var(--text-faint);">Registered via <code>IRuleFunction</code> on the host application. The built-in Rulewright.Extensions.Functions catalog is registered for Test rule.</div>`;
    } else if(node.type === 'action'){
      const valueWired = !!node.inputs[1];
      html += `<div class="field"><label>Action type</label><select id="cfgType">${ACTION_TYPES.map(t=>`<option value="${t}" ${t===node.config.type?'selected':''}>${t}</option>`).join('')}</select></div>
        <div class="field"><label>Branch</label><select id="cfgBranch">
          <option value="then" ${node.config.branch!=='else'?'selected':''}>Then (condition is true)</option>
          <option value="else" ${node.config.branch==='else'?'selected':''}>Else (condition is false)</option>
        </select></div>
        <div class="field"><label>Output target</label><input type="text" id="cfgTarget" placeholder="Discount" value="${escapeHtml(node.config.target)}"></div>`;
      if(node.config.type !== 'removeOutput'){
        html += `<div class="field" id="valueFieldWrap"><label>Value (constant)</label><input type="text" id="cfgValue" placeholder="10" value="${escapeHtml(node.config.value)}" ${valueWired?'disabled':''}></div>`;
        if(valueWired){
          html += `<div style="font-size:10.5px;color:var(--text-faint);margin:-6px 0 11px;">Using the wired <strong>Value (expr)</strong> computed value instead — disconnect that pin to type a constant.</div>`;
        }
      } else {
        html += `<div style="font-size:10.5px;color:var(--text-faint);">removeOutput takes no value — it just deletes the target key.</div>`;
      }
    } else if(node.type === 'valLiteral'){
      html += `<div class="field"><label>Value (JSON)</label><input type="text" id="cfgValue" placeholder='10 or "gold" or true' value="${escapeHtml(node.config.value)}"></div>`;
    } else if(node.type === 'valField'){
      html += `<div class="field"><label>Field path</label><input type="text" id="cfgField" placeholder="Order.Total" value="${escapeHtml(node.config.field)}"></div>`;
    } else if(node.type === 'valOp'){
      const arity = EXPR_ARITY[node.config.operator];
      html += `<div class="field"><label>Operator</label><select id="cfgOperator">${EXPR_OPERATORS.map(o=>`<option value="${o}" ${o===node.config.operator?'selected':''}>${o}</option>`).join('')}</select></div>
        <div style="font-size:10.5px;color:var(--text-faint);">${arity ? `Takes exactly ${arity.min} operand${arity.min===1?'':'s'}.` : 'Takes two or more operands.'} Wire Literal/Field Ref/Expression nodes into the operand pins below.</div>`;
    } else {
      html += `<div style="font-size:11px;color:var(--text-muted);line-height:1.6;">${def.label} combines every connected input.
        Drag a wire into the empty slot below the last input to add another branch.</div>`;
    }

    body.innerHTML = html;

    if(node.type === 'trigger'){
      document.getElementById('factEditor').addEventListener('change', (e)=>{
        try{
          state.sampleFact = JSON.parse(e.target.value);
          renderNode(node);
          showToast("Sample fact updated.");
        }catch(err){ showToast("Invalid JSON — fact not updated."); }
      });
    }
    if(node.type === 'leaf'){
      const fEl = document.getElementById('cfgField');
      if(fEl) fEl.addEventListener('input', (e)=>{ node.config.field=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgOperator').addEventListener('change', (e)=>{
        node.config.operator = e.target.value;
        renderInspector();
        renderNode(node); regenerateJson();
      });
      const vf = document.getElementById('cfgValue');
      if(vf) vf.addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'function'){
      document.getElementById('cfgName').addEventListener('input', (e)=>{ node.config.name=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgField').addEventListener('input', (e)=>{ node.config.field=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgValue').addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'action'){
      document.getElementById('cfgType').addEventListener('change', (e)=>{ node.config.type=e.target.value; renderInspector(); renderNode(node); regenerateJson(); });
      document.getElementById('cfgBranch').addEventListener('change', (e)=>{ node.config.branch=e.target.value; renderNode(node); regenerateJson(); });
      document.getElementById('cfgTarget').addEventListener('input', (e)=>{ node.config.target=e.target.value; renderNode(node); regenerateJson(); });
      const vf = document.getElementById('cfgValue');
      if(vf) vf.addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'valLiteral'){
      document.getElementById('cfgValue').addEventListener('input', (e)=>{ node.config.value=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'valField'){
      document.getElementById('cfgField').addEventListener('input', (e)=>{ node.config.field=e.target.value; renderNode(node); regenerateJson(); });
    }
    if(node.type === 'valOp'){
      document.getElementById('cfgOperator').addEventListener('change', (e)=>{
        node.config.operator = e.target.value;
        const arity = EXPR_ARITY[e.target.value];
        if(arity){
          while(node.inputs.filter(Boolean).length < arity.min && node.inputs.length < arity.min) node.inputs.push(null);
          while(node.inputs.length > arity.max + 1) node.inputs.pop();
        }
        renderNode(node); regenerateJson();
      });
    }
  }

  /* ============================================================
     Mouse interaction: pan / drag / connect
     ============================================================ */
  function onNodeMouseDown(e){
    if(e.target.classList.contains('port') || e.target.classList.contains('node-del')) return;
    const nodeEl = e.currentTarget;
    const id = nodeEl.dataset.id;
    const node = state.nodes.get(id);
    const startWorld = screenToWorld(e.clientX, e.clientY);
    state.dragNode = { id, offsetX: node.x - startWorld.x, offsetY: node.y - startWorld.y };
    selectNode(id);
    e.stopPropagation();
  }

  function initMouseHandlers(){
    canvasWrap.addEventListener('mousedown', (e)=>{
      if(e.target !== canvasWrap && e.target.id !== 'world' && !e.target.classList.contains('canvas-bg')) return;
      clearSelection();
      state.panDrag = { startX:e.clientX, startY:e.clientY, startPanX:state.panX, startPanY:state.panY };
    });

    window.addEventListener('mousemove', (e)=>{
      if(state.dragNode){
        const w = screenToWorld(e.clientX, e.clientY);
        const node = state.nodes.get(state.dragNode.id);
        node.x = w.x + state.dragNode.offsetX;
        node.y = w.y + state.dragNode.offsetY;
        renderNode(node);
        renderWires();
      } else if(state.panDrag){
        state.panX = state.panDrag.startPanX + (e.clientX - state.panDrag.startX);
        state.panY = state.panDrag.startPanY + (e.clientY - state.panDrag.startY);
        applyWorldTransform();
      } else if(state.pendingWire){
        const w = screenToWorld(e.clientX, e.clientY);
        state.pendingWire.x = w.x;
        state.pendingWire.y = w.y;
        renderWires();
      }
    });

    window.addEventListener('mouseup', ()=>{
      state.dragNode = null;
      state.panDrag = null;
      if(state.pendingWire){
        state.pendingWire = null;
        renderWires();
      }
    });

    canvasWrap.addEventListener('wheel', (e)=>{
      e.preventDefault();
      const rect = canvasWrap.getBoundingClientRect();
      const cx = e.clientX - rect.left, cy = e.clientY - rect.top;
      const wx = (cx - state.panX)/state.scale, wy = (cy - state.panY)/state.scale;
      state.scale = clamp(state.scale * (e.deltaY < 0 ? 1.08 : 0.92), 0.35, 2);
      state.panX = cx - wx*state.scale;
      state.panY = cy - wy*state.scale;
      applyWorldTransform();
    }, { passive:false });

    document.getElementById('zoomIn').addEventListener('click', ()=>{
      state.scale = clamp(state.scale*1.15, 0.35, 2); applyWorldTransform();
    });
    document.getElementById('zoomOut').addEventListener('click', ()=>{
      state.scale = clamp(state.scale*0.87, 0.35, 2); applyWorldTransform();
    });
    document.getElementById('zoomReset').addEventListener('click', ()=>{
      state.scale = 1; state.panX = 60; state.panY = 60; applyWorldTransform();
    });

    window.addEventListener('keydown', (e)=>{
      if(['INPUT','TEXTAREA','SELECT'].includes(document.activeElement.tagName)) return;
      if(e.key === 'Delete' || e.key === 'Backspace'){
        if(state.selectedNode){ removeNode(state.selectedNode); }
        else if(state.selectedConn){ removeConnection(state.selectedConn); }
      }
    });
  }

  function startWireDrag(fromNodeId, e){
    const w = screenToWorld(e.clientX, e.clientY);
    state.pendingWire = { fromNode: fromNodeId, x:w.x, y:w.y };
    renderWires();
  }
  function completeWireDrag(toNodeId, toIdx){
    if(!state.pendingWire) return;
    addConnection(state.pendingWire.fromNode, toNodeId, toIdx);
    state.pendingWire = null;
    renderWires();
  }

  /* ============================================================
     Drag-drop from palette
     ============================================================ */
  function initPalette(){
    document.querySelectorAll('.palette-item').forEach(item=>{
      item.addEventListener('dragstart', (e)=>{
        e.dataTransfer.setData('text/plain', item.dataset.type);
      });
      item.addEventListener('dblclick', ()=>{
        const w = screenToWorld(canvasWrap.clientWidth/2 + Math.random()*40, canvasWrap.clientHeight/2 + Math.random()*40);
        createNode(item.dataset.type, w.x, w.y);
        renderWires();
        regenerateJson();
      });
    });
    canvasWrap.addEventListener('dragover', (e)=>e.preventDefault());
    canvasWrap.addEventListener('drop', (e)=>{
      e.preventDefault();
      const type = e.dataTransfer.getData('text/plain');
      if(!type || !TYPE_DEFS[type]) return;
      const w = screenToWorld(e.clientX, e.clientY);
      createNode(type, w.x - NODE_W/2, w.y - 20);
      renderWires();
      regenerateJson();
    });
  }

  /* ============================================================
     JSON generation
     ============================================================ */
  function parseValue(raw){
    if(raw === undefined || raw === "") return "";
    const trimmed = String(raw).trim();
    if(trimmed === 'true') return true;
    if(trimmed === 'false') return false;
    if(trimmed === 'null') return null;
    if(!isNaN(Number(trimmed)) && trimmed !== '') return Number(trimmed);
    try{
      if(trimmed.startsWith('[') || trimmed.startsWith('{')) return JSON.parse(trimmed);
    }catch(err){ /* fall through to string */ }
    return String(raw); // preserve meaningful leading/trailing whitespace (e.g. "Thanks " in a concat)
  }

  // A computed-value node tree (Literal/Field Ref/Expression) -> the JSON schema's value-
  // expression shape ({literal}/{field}/{op,operands}). Used for an Action's Value (expr) pin
  // and a Compare's Field (expr) pin — the ONLY two places the schema allows a computed value
  // (a condition leaf's comparison `value` must stay a constant; RuleSetValidator rejects an
  // object there).
  function buildValueExpressionTree(nodeId, warnings, seen){
    seen = seen || new Set();
    const node = state.nodes.get(nodeId);
    if(!node) return null;
    if(seen.has(nodeId)) return null;
    seen.add(nodeId);

    if(node.type === 'valLiteral'){
      return { literal: parseValue(node.config.value) };
    }
    if(node.type === 'valField'){
      if(!node.config.field) warnings.push(`${node.tag}: missing field path`);
      return { field: node.config.field || "" };
    }
    if(node.type === 'valOp'){
      const childConns = node.inputs.filter(Boolean);
      if(childConns.length === 0){
        warnings.push(`${node.tag}: ${node.config.operator} has no connected operands`);
        return null;
      }
      const operands = childConns.map(connId=>{
        const conn = state.connections.get(connId);
        return buildValueExpressionTree(conn.from, warnings, seen);
      }).filter(Boolean);
      return { op: node.config.operator, operands };
    }
    warnings.push(`A ${node.tag} node can't be used as a computed value here.`);
    return null;
  }

  // Builds both the JSON-schema condition object AND a parallel "id tree" of the same shape
  // (node id in place of the JSON body) so a later trace result — which mirrors this exact
  // shape (see Rulewright.Execution.ConditionTraceBuilder) — can be zipped against it without
  // any string-matching heuristics.
  function buildConditionTree(nodeId, warnings, seen){
    seen = seen || new Set();
    const node = state.nodes.get(nodeId);
    if(!node) return null;
    if(seen.has(nodeId)) return null;
    seen.add(nodeId);

    if(node.type === 'leaf'){
      const leaf = { operator: node.config.operator };
      const fieldExprConn = node.inputs[0];
      if(fieldExprConn){
        const conn = state.connections.get(fieldExprConn);
        const expr = buildValueExpressionTree(conn.from, warnings, seen);
        if(expr) leaf.expression = expr;
        else warnings.push(`${node.tag}: field expression is incomplete`);
      } else {
        if(!node.config.field) warnings.push(`${node.tag}: missing field path`);
        leaf.field = node.config.field || "";
      }
      if(!['IsNull','IsNotNull'].includes(node.config.operator)){
        leaf.value = parseValue(node.config.value);
      }
      return { json:leaf, idTree:{ id:node.id, children:[] } };
    }
    if(node.type === 'function'){
      if(!node.config.name) warnings.push(`${node.tag}: missing function name`);
      const fn = { operator:'custom', name: node.config.name||"" };
      if(node.config.field) fn.field = node.config.field;
      if(node.config.value !== undefined && node.config.value !== "") fn.value = parseValue(node.config.value);
      return { json: fn, idTree:{ id:node.id, children:[] } };
    }
    if(node.type === 'and' || node.type === 'or' || node.type === 'not'){
      const def = TYPE_DEFS[node.type];
      const childConns = node.inputs.filter(Boolean);
      if(childConns.length === 0){
        warnings.push(`${node.tag}: ${def.label} has no connected inputs`);
        return null;
      }
      const built = childConns.map(connId=>{
        const conn = state.connections.get(connId);
        return buildConditionTree(conn.from, warnings, seen);
      }).filter(Boolean);
      return {
        json: { type:'group', operator: def.operator, rules: built.map(b=>b.json) },
        idTree: { id:node.id, children: built.map(b=>b.idTree) }
      };
    }
    return null;
  }

  function buildRuleJson(){
    const warnings = [];
    const actionNodes = [...state.nodes.values()].filter(n=>n.type==='action');
    if(actionNodes.length === 0){
      warnings.push("No Action is connected — add one to complete the rule.");
    }

    let conditionBuilt = null;
    const rootSources = new Set();
    actionNodes.forEach(a=>{
      const connId = a.inputs[0];
      if(!connId){ warnings.push(`${a.tag}: not connected to a condition`); return; }
      const conn = state.connections.get(connId);
      rootSources.add(conn.from);
    });
    if(rootSources.size > 1){
      warnings.push("Multiple action nodes trace back to different condition branches. Only the first branch is used below — connect every action to the same condition node for one rule.");
    }
    const rootId = [...rootSources][0];
    if(rootId){ conditionBuilt = buildConditionTree(rootId, warnings); }
    if(rootId && !conditionBuilt){ warnings.push("The connected condition graph is incomplete."); }

    const actionIds = [];
    const actions = [];
    const elseActions = [];
    actionNodes.filter(a=>a.inputs[0]).forEach(a=>{
      actionIds.push(a.id);
      if(!a.config.target) warnings.push(`${a.tag}: missing output target`);
      const entry = { type: a.config.type || 'setOutput', target: a.config.target||"" };
      if(entry.type !== 'removeOutput'){
        const valueExprConn = a.inputs[1];
        if(valueExprConn){
          const conn = state.connections.get(valueExprConn);
          const expr = buildValueExpressionTree(conn.from, warnings, new Set());
          entry.value = expr !== null ? expr : parseValue(a.config.value);
        } else {
          entry.value = parseValue(a.config.value);
        }
      }
      (a.config.branch === 'else' ? elseActions : actions).push(entry);
    });

    const ruleIdEl = document.getElementById('ruleId');
    const rule = {
      id: (ruleIdEl && ruleIdEl.value) || "untitled-rule",
      description: (document.getElementById('ruleDescription')||{}).value || undefined,
      priority: Number((document.getElementById('rulePriority')||{}).value) || 0,
      enabled: document.getElementById('ruleEnabled') ? document.getElementById('ruleEnabled').checked : true,
      condition: conditionBuilt ? conditionBuilt.json : { field:"", operator:"IsNotNull" },
      actions
    };
    if(elseActions.length > 0){ rule.else = elseActions; }
    if(!conditionBuilt){
      warnings.push("No condition is connected to any action — the rule can't be evaluated yet.");
    }

    return { rule, warnings, idTree: conditionBuilt ? conditionBuilt.idTree : null, actionIds };
  }

  /* ============================================================
     JSON syntax highlighting (for the drawer's JSON tab)
     ============================================================ */
  function renderJsonHighlighted(value, indent){
    indent = indent || 0;
    const pad = '  '.repeat(indent);
    const pad2 = '  '.repeat(indent+1);
    if(value === null || value === undefined) return `<span class="jv-null">null</span>`;
    if(typeof value === 'string') return `<span class="jv-str">"${escapeHtml(value)}"</span>`;
    if(typeof value === 'number') return `<span class="jv-num">${value}</span>`;
    if(typeof value === 'boolean') return `<span class="jv-bool">${value}</span>`;
    if(Array.isArray(value)){
      if(value.length === 0) return '[]';
      const items = value.map(v=>pad2 + renderJsonHighlighted(v, indent+1)).join(',\n');
      return `[\n${items}\n${pad}]`;
    }
    if(typeof value === 'object'){
      const keys = Object.keys(value).filter(k=>value[k] !== undefined);
      if(keys.length === 0) return '{}';
      const items = keys.map(k=>`${pad2}<span class="jk">"${escapeHtml(k)}"</span>: ${renderJsonHighlighted(value[k], indent+1)}`).join(',\n');
      return `{\n${items}\n${pad}}`;
    }
    return String(value);
  }

  function regenerateJson(){
    const built = buildRuleJson();
    document.getElementById('jsonOut').innerHTML = renderJsonHighlighted(built.rule, 0);
    renderWarnings(built.warnings);
    return built;
  }

  function renderWarnings(warnings){
    const list = document.getElementById('warnList');
    if(warnings.length === 0){
      list.className = 'warn-list ok';
      list.innerHTML = `<li><span class="dot">●</span> Rule graph looks complete.</li>`;
      return;
    }
    list.className = 'warn-list';
    list.innerHTML = warnings.map(w=>`<li><span class="dot">●</span>${escapeHtml(w)}</li>`).join('');
  }

  /* ============================================================
     Drawer & modal chrome
     ============================================================ */
  function openDrawer(tab){
    document.getElementById('drawer').classList.add('open');
    if(tab) switchDrawerTab(tab);
  }
  function closeDrawer(){
    document.getElementById('drawer').classList.remove('open');
  }
  function switchDrawerTab(tab){
    document.querySelectorAll('.drawer-tab').forEach(b=>b.classList.toggle('active', b.dataset.tab===tab));
    document.querySelectorAll('.drawer-panel').forEach(p=>p.classList.remove('active'));
    document.getElementById('panel'+tab.charAt(0).toUpperCase()+tab.slice(1)).classList.add('active');
  }

  function initDrawer(){
    document.querySelectorAll('.drawer-tab').forEach(btn=>{
      btn.addEventListener('click', ()=>switchDrawerTab(btn.dataset.tab));
    });
    document.getElementById('drawerClose').addEventListener('click', closeDrawer);
    document.getElementById('btnCopyJson').addEventListener('click', async ()=>{
      const built = buildRuleJson();
      try{
        await navigator.clipboard.writeText(JSON.stringify(built.rule, null, 2));
        showToast("Rule JSON copied to clipboard.");
      }catch(err){ showToast("Couldn't access the clipboard."); }
    });
  }

  function initModal(){
    document.getElementById('btnTest').addEventListener('click', ()=>{
      document.getElementById('factInput').value = JSON.stringify(state.sampleFact, null, 2);
      document.getElementById('testModal').classList.add('open');
    });
    document.getElementById('testModalClose').addEventListener('click', closeTestModal);
    document.getElementById('testCancel').addEventListener('click', closeTestModal);
    document.getElementById('testRun').addEventListener('click', runTest);
  }
  function closeTestModal(){
    document.getElementById('testModal').classList.remove('open');
  }

  function showResultBanner(cls, text){
    resultBanner.className = 'result-banner show ' + cls;
    resultBanner.innerHTML = `<span>${escapeHtml(text)}</span><button class="rb-close">×</button>`;
    resultBanner.querySelector('.rb-close').addEventListener('click', ()=>{
      resultBanner.classList.remove('show');
    });
  }

  function clearTestHighlight(){
    state.nodes.forEach(n=>{ n._test = undefined; renderNode(n); });
    renderWires();
  }

  function applyTraceHighlight(idTree, traceNode){
    if(!idTree) return;
    const node = state.nodes.get(idTree.id);
    if(node && traceNode){
      node._test = traceNode.passed === null || traceNode.passed === undefined ? undefined : traceNode.passed;
    }
    (idTree.children||[]).forEach((childIdTree, i)=>{
      const childTrace = traceNode && traceNode.children ? traceNode.children[i] : null;
      applyTraceHighlight(childIdTree, childTrace);
    });
  }

  async function runTest(){
    const built = regenerateJson();
    let fact;
    try{
      fact = JSON.parse(document.getElementById('factInput').value);
    }catch(err){
      showToast("Fact JSON is invalid.");
      return;
    }
    state.sampleFact = fact;

    if(!dotNetRef){ showToast("Engine bridge not ready."); return; }

    const ruleJson = JSON.stringify(built.rule);
    const factJson = JSON.stringify(fact);
    const responseText = await dotNetRef.invokeMethodAsync('EvaluateRule', ruleJson, factJson);
    const response = JSON.parse(responseText);

    closeTestModal();
    clearTestHighlight();

    if(!response.ok){
      showResultBanner('fail', 'Error: ' + response.error);
      openDrawer('validation');
      return;
    }

    applyTraceHighlight(built.idTree, response.trace);
    built.actionIds.forEach(id=>{
      const n = state.nodes.get(id);
      if(n){ n._test = response.fired; renderNode(n); }
    });
    renderWires();

    const outputsText = response.outputs && Object.keys(response.outputs).length
      ? Object.entries(response.outputs).map(([k,v])=>`${k}=${JSON.stringify(v)}`).join(', ')
      : '(no outputs)';
    showResultBanner(response.fired ? 'pass' : 'fail', (response.fired ? '✓ Rule fired — ' : '✗ Rule did not fire — ') + outputsText);

    renderTracePanel(response.trace);
    openDrawer('trace');
  }

  function renderTracePanel(traceNode){
    const container = document.getElementById('panelTrace');
    if(!traceNode){
      container.innerHTML = `<div class="insp-empty">No condition to trace.</div>`;
      return;
    }
    const lines = [];
    (function walk(node, d){
      if(!node) return;
      const passed = node.passed;
      const cls = passed === true ? 'pass' : passed === false ? 'fail' : '';
      const detail = passed === null || passed === undefined ? 'short-circuited' : (passed ? 'passed' : 'failed');
      lines.push(`<div class="trace-line" style="padding-left:${d*14}px;">
        <span class="trace-dot ${cls}"></span>
        <span class="trace-node">${escapeHtml(node.description)}</span>
        <span class="trace-detail">${detail}</span>
      </div>`);
      (node.children||[]).forEach(c=>walk(c, d+1));
    })(traceNode, 0);
    container.innerHTML = lines.join('') || `<div class="insp-empty">No condition to trace.</div>`;
  }

  async function runValidate(){
    const built = regenerateJson();
    if(!dotNetRef){ showToast("Engine bridge not ready."); return; }
    const responseText = await dotNetRef.invokeMethodAsync('ValidateRule', JSON.stringify(built.rule));
    const response = JSON.parse(responseText);

    const list = document.getElementById('warnList');
    const combined = [...built.warnings];
    response.errors.forEach(e=>combined.push((e.path ? e.path + ': ' : '') + e.message));

    if(combined.length === 0){
      list.className = 'warn-list ok';
      list.innerHTML = `<li><span class="dot">●</span> Rule is structurally valid.</li>`;
    } else {
      list.className = 'warn-list';
      list.innerHTML = combined.map(w=>`<li><span class="dot">●</span>${escapeHtml(w)}</li>`).join('');
    }
    openDrawer('warnings');
    showToast(response.valid && built.warnings.length===0 ? "Rule is valid." : "Validation found issues — see the Validation tab.");
  }

  function initToolbar(){
    document.getElementById('btnValidate').addEventListener('click', runValidate);
    document.getElementById('btnExport').addEventListener('click', ()=>{ regenerateJson(); openDrawer('json'); });
  }

  /* ============================================================
     Examples: fetch + import rule JSON as a canvas graph
     ============================================================ */
  function initExamples(){
    const select = document.getElementById('exampleSelect');
    if(!select) return;
    fetch('examples/manifest.json').then(r=>r.json()).then(list=>{
      list.forEach(e=>{
        const opt = document.createElement('option');
        opt.value = e.file;
        opt.title = e.description || '';
        opt.textContent = e.file;
        select.appendChild(opt);
      });
    }).catch(()=>{ /* examples are optional; a missing manifest just leaves the dropdown empty */ });

    select.addEventListener('change', async (e)=>{
      const file = e.target.value;
      if(!file) return;
      try{
        const res = await fetch('examples/' + file);
        const doc = await res.json();
        if(doc.decisionTable){
          showToast("Decision tables aren't supported by this visual canvas yet — open it in a text editor instead.");
        } else if(Array.isArray(doc.rules)){
          importRuleJson(doc.rules[0]);
          showToast(`Loaded rule 1 of ${doc.rules.length} from "${file}" — this canvas builds one rule at a time.`);
        } else {
          importRuleJson(doc);
          showToast(`Loaded "${file}".`);
        }
      }catch(err){
        showToast("Couldn't load that example.");
      }
      select.value = "";
    });
  }

  const COL_W = 260, ROW_H = 130;

  // Text fields (config.value) are parsed by parseValue(), which treats bare unquoted text as
  // a string — matching what a user types by hand ("gold", not "\"gold\""). Populating a field
  // with JSON.stringify(value) for a string would wrap it in quotes the field's own parser
  // doesn't expect, double-quoting it on the next export. Only non-string values need their
  // JSON form (numbers/booleans serialize the same either way; arrays/objects need brackets for
  // parseValue's JSON.parse fallback to kick in).
  function valueToFieldText(value){
    return typeof value === 'string' ? value : JSON.stringify(value);
  }

  function importValueExpr(expr, x, y){
    if(expr === null || typeof expr !== 'object'){
      const n = createNode('valLiteral', x, y);
      n.config.value = valueToFieldText(expr);
      renderNode(n);
      return n;
    }
    if('literal' in expr){
      const n = createNode('valLiteral', x, y);
      n.config.value = valueToFieldText(expr.literal);
      renderNode(n);
      return n;
    }
    if('field' in expr){
      const n = createNode('valField', x, y);
      n.config.field = expr.field;
      renderNode(n);
      return n;
    }
    if('op' in expr){
      const n = createNode('valOp', x, y);
      n.config.operator = expr.op;
      const operands = expr.operands || [];
      // Don't pre-grow node.inputs here — addConnection() already appends a fresh empty slot
      // on every call for a dynamicInput node (that's how manual click-to-wire keeps exactly
      // one trailing empty slot). Pre-growing AND relying on that auto-grow compounds into
      // extra unwired slots (e.g. 3 operands ending up with 6 slots).
      let oy = y;
      operands.forEach((operand, i)=>{
        const child = importValueExpr(operand, x - COL_W, oy);
        oy += ROW_H;
        addConnection(child.id, n.id, i);
      });
      renderNode(n);
      return n;
    }
    const n = createNode('valLiteral', x, y);
    n.config.value = 'null';
    renderNode(n);
    return n;
  }

  function importCondition(cond, x, y){
    if(cond.type === 'group'){
      const opType = { AND:'and', OR:'or', NOT:'not' }[cond.operator] || 'and';
      const n = createNode(opType, x, y);
      const rules = cond.rules || [];
      // See the matching comment in importValueExpr — no pre-growth, addConnection() grows it.
      let cy = y;
      const childIds = [];
      rules.forEach(childCond=>{
        const childResult = importCondition(childCond, x - COL_W, cy);
        cy += ROW_H;
        childIds.push(childResult);
      });
      childIds.forEach((child, i)=>addConnection(child.id, n.id, i));
      renderNode(n);
      return n;
    }
    if(cond.operator === 'custom'){
      const n = createNode('function', x, y);
      n.config.name = cond.name || '';
      if(cond.field) n.config.field = cond.field;
      if(cond.value !== undefined) n.config.value = valueToFieldText(cond.value);
      renderNode(n);
      return n;
    }
    const n = createNode('leaf', x, y);
    n.config.operator = cond.operator || 'Equals';
    if(cond.expression){
      const exprNode = importValueExpr(cond.expression, x - COL_W, y);
      addConnection(exprNode.id, n.id, 0);
    } else {
      n.config.field = cond.field || '';
    }
    if(cond.value !== undefined) n.config.value = JSON.stringify(cond.value);
    renderNode(n);
    return n;
  }

  function importRuleJson(rule){
    clearGraph();

    document.getElementById('ruleId').value = rule.id || 'imported-rule';
    document.getElementById('ruleDescription').value = rule.description || '';
    document.getElementById('rulePriority').value = rule.priority || 0;
    document.getElementById('ruleEnabled').checked = rule.enabled !== false;

    createNode('trigger', 40, 260);

    let rootNode = null;
    if(rule.condition){
      rootNode = importCondition(rule.condition, 340, 260);
    }

    const actionX = 340 + COL_W * 3;
    let ay = 40;
    const allActions = [
      ...(rule.actions||[]).map(a=>({ ...a, branch:'then' })),
      ...(rule.else||[]).map(a=>({ ...a, branch:'else' })),
    ];
    allActions.forEach(a=>{
      const n = createNode('action', actionX, ay);
      n.config.type = a.type || 'setOutput';
      n.config.target = a.target || '';
      n.config.branch = a.branch;
      if(a.type !== 'removeOutput' && a.value !== undefined){
        if(a.value !== null && typeof a.value === 'object'){
          const exprNode = importValueExpr(a.value, actionX - COL_W, ay);
          addConnection(exprNode.id, n.id, 1);
        } else {
          n.config.value = valueToFieldText(a.value);
        }
      }
      renderNode(n);
      if(rootNode) addConnection(rootNode.id, n.id, 0);
      ay += ROW_H;
    });

    renderWires();
    regenerateJson();
  }

  /* ============================================================
     Seed graph (nicer first run than a blank canvas)
     ============================================================ */
  function seedGraph(){
    const trigger = createNode('trigger', 40, 60);
    const leaf = createNode('leaf', 340, 40);
    leaf.config.field = 'Customer.Age';
    leaf.config.operator = 'GreaterThan';
    leaf.config.value = '18';
    const action = createNode('action', 640, 60);
    action.config.target = 'Discount';
    action.config.value = '10';
    renderNode(trigger); renderNode(leaf); renderNode(action);
    addConnection(leaf.id, action.id, 0);
  }

  /* ============================================================
     Public init
     ============================================================ */
  function init(reference){
    dotNetRef = reference;
    canvasWrap = document.getElementById('canvasWrap');
    world = document.getElementById('world');
    wiresSvg = document.getElementById('wiresSvg');
    toastEl = document.getElementById('toast');
    resultBanner = document.getElementById('resultBanner');

    applyWorldTransform();
    initMouseHandlers();
    initPalette();
    initDrawer();
    initModal();
    initToolbar();
    initExamples();

    ['ruleId','ruleDescription','rulePriority','ruleEnabled'].forEach(id=>{
      const el = document.getElementById(id);
      if(el) el.addEventListener('input', ()=>regenerateJson());
    });

    seedGraph();
    regenerateJson();
  }

  return { init };
})();
