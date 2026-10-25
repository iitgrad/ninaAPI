"""Blender authoring source for Starfront's lightweight interactive rig model.
Run: blender -b --python scripts/build-mount-model.py -- /absolute/output.json
Coordinates: Blender Z-up; optical assembly points +Y. Dimensions are illustrative.
"""
import bpy, math, sys, json, struct, base64
from pathlib import Path
from mathutils import Vector
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
materials={}
def mat(name,color,metal=.35,rough=.35):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*color,1);bs.inputs['Metallic'].default_value=metal;bs.inputs['Roughness'].default_value=rough
 materials[name]={'color':color,'metalness':metal,'roughness':rough};return m
red=mat('Anodized carmine',(.55,.024,.045),.65,.28)
red2=mat('Red edge detail',(.82,.045,.064),.5,.25)
black=mat('Satin graphite',(.028,.036,.047),.5,.3)
rubber=mat('Rubber grip',(.012,.016,.02),.05,.65)
silver=mat('Machined aluminum',(.52,.60,.67),.78,.24)
pier=mat('Powder coated pier',(.12,.17,.21),.4,.52)
glass=mat('Multicoated objective',(.025,.13,.18),.8,.12)
white=mat('Laser etched lettering',(.82,.88,.88),.2,.4)
objects=[]
def finish(o,name,material,group='fixed',bevel=0):
 o.name=name;o.data.materials.append(material);o['rig_group']=group
 if bevel:
  m=o.modifiers.new('Machined edge radius','BEVEL');m.width=bevel;m.segments=3
  m=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
 objects.append(o);return o

def cyl(name,loc,r,length,material,axis=(0,0,1),group='fixed',vertices=48,bevel=.003):
 bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=length,location=loc)
 o=bpy.context.object;o.rotation_euler=Vector(axis).to_track_quat('Z','Y').to_euler()
 for p in o.data.polygons:p.use_smooth=len(p.vertices)==4
 return finish(o,name,material,group,bevel)
def box(name,loc,size,material,group='fixed',bevel=.01):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 return finish(o,name,material,group,bevel)
def ring(name,loc,major,minor,material,axis=(0,1,0),group='optics'):
 bpy.ops.mesh.primitive_torus_add(major_radius=major,minor_radius=minor,major_segments=48,minor_segments=8,location=loc)
 o=bpy.context.object;o.rotation_euler=Vector(axis).to_track_quat('Z','Y').to_euler()
 for p in o.data.polygons:p.use_smooth=True
 return finish(o,name,material,group)
def label(text,loc,size,material,rotation=(math.pi/2,0,0),group='optics'):
 bpy.ops.object.text_add(location=loc);o=bpy.context.object;o.data.body=text;o.data.size=size;o.data.align_x='CENTER';o.data.extrude=.0003;o.rotation_euler=rotation
 bpy.ops.object.convert(target='MESH');return finish(bpy.context.object,text,material,group)
# Permanent floor-mounted pier, flush plate and four anchor studs. No legs/feet.
box('Flush floor anchor plate',(0,0,.025),(.5,.5,.05),pier,bevel=.018)
cyl('Permanent steel pier',(0,0,.56),.145,1.07,pier,bevel=.009)
cyl('Top mounting flange',(0,0,1.11),.21,.045,silver)
cyl('AM5 adapter',(0,0,1.16),.15,.07,black)
for x in [-.19,.19]:
 for y in [-.19,.19]:
  cyl('Anchor washer',(x,y,.055),.03,.009,silver,vertices=24)
  cyl('Hex anchor nut',(x,y,.071),.019,.026,silver,vertices=6,bevel=.001)
  cyl('Anchor stud',(x,y,.089),.008,.018,black,vertices=16,bevel=0)
# AM5N harmonic mount head and altitude adjustment.
box('AM5N latitude cradle',(0,0,1.25),(.31,.26,.13),black)
head=box('AM5N harmonic housing',(0,-.018,1.41),(.29,.30,.29),red,bevel=.035);head.rotation_euler[0]=math.radians(-28)
cyl('Altitude bearing left',(-.17,0,1.31),.09,.04,black,axis=(1,0,0))
cyl('Altitude adjuster',(.19,0,1.31),.055,.055,red2,axis=(1,0,0))
for i in range(12):
 a=i*math.tau/12;cyl('Adjuster grip',(.215,.047*math.cos(a),1.31+.047*math.sin(a)),.006,.025,black,axis=(1,0,0),vertices=8,bevel=0)
cyl('Declination bearing',(0,0,1.57),.13,.10,black)
cyl('Declination trim',(0,0,1.62),.13,.018,red2)
label('AM5N',(0,-.184,1.41),.047,white,group='fixed')
# Optical axis pivot at1.72m; tube extends towards +Y.
Z=1.72
box('Losmandy saddle',(0,0,Z-.095),(.28,.24,.07),black,'optics')
box('Red dovetail',(0,0,Z-.055),(.14,.59,.033),red2,'optics',.005)
for y in [-.085,.085]:
 cyl('Saddle clamping screw',(.18,y,Z-.093),.022,.065,silver,axis=(1,0,0),group='optics',vertices=24)
 cyl('Saddle hand knob',(.22,y,Z-.093),.033,.027,black,axis=(1,0,0),group='optics',vertices=24)
cyl('RedCat91 main optical tube',(0,.055,Z+.12),.13,.60,red,axis=(0,1,0),group='optics')
# Large dew shield, recessed coated objective, and rim.
cyl('Retractable dew shield',(0,.37,Z+.12),.154,.25,red,axis=(0,1,0),group='optics')
cyl('Dew shield inner baffle',(0,.498,Z+.12),.143,.009,black,axis=(0,1,0),group='optics',bevel=0)
cyl('Recessed objective',(0,.505,Z+.12),.122,.003,glass,axis=(0,1,0),group='optics',bevel=0)
ring('Black dew shield lip',(0,.507,Z+.12),.146,.008,black)
ring('Objective retaining ring',(0,.508,Z+.12),.125,.004,silver)
ring('Front red bevel',(0,.48,Z+.12),.152,.004,red2)
# Tube rings, top rail and compact guide scope.
for y in [-.17,.16]:
 ring('Black CNC tube ring',(0,y,Z+.12),.133,.013,black)
 box('Tube ring foot',(0,y,Z-.014),(.15,.055,.065),black,'optics',.008)
 box('Tube ring top',(0,y,Z+.26),(.13,.06,.032),black,'optics',.005)
 for x in [-.05,.05]:cyl('Ring fastener',(x,y,Z+.282),.009,.008,silver,group='optics',vertices=12,bevel=0)
box('Guide scope top rail',(0,0,Z+.293),(.085,.47,.035),red2,'optics',.007)
for y in [-.11,.11]:box('Guide scope foot',(0,y,Z+.34),(.06,.04,.085),black,'optics',.005)
cyl('UniGuide32 body',(0,.04,Z+.40),.047,.30,red,axis=(0,1,0),group='optics')
cyl('Guide lens hood',(0,.21,Z+.40),.052,.08,black,axis=(0,1,0),group='optics')
cyl('Guide coated lens',(0,.251,Z+.40),.043,.002,glass,axis=(0,1,0),group='optics',bevel=0)
cyl('ASI220MM Mini',(0,-.16,Z+.40),.032,.10,red2,axis=(0,1,0),group='optics')
# WIFD focuser collar and side-mounted EAF.
cyl('WIFD focuser rear',(0,-.30,Z+.12),.112,.12,black,axis=(0,1,0),group='optics')
for y in [-.25,-.27,-.29,-.31,-.33,-.35]:ring('Focuser grip ridge',(0,y,Z+.12),.111,.0035,rubber)
box('EAF Pro motor',(.175,-.22,Z+.085),(.115,.125,.14),red,'optics',.015)
box('EAF bracket',(.115,-.22,Z+.01),(.14,.15,.018),black,'optics',.003)
cyl('Camera angle adjuster',(0,-.395,Z+.12),.085,.065,red2,axis=(0,1,0),group='optics')
# Offset filter wheel housing and cooled ASI6200 camera.
cyl('EFW7 filter wheel',(0.055,-.46,Z+.12),.19,.065,black,axis=(0,1,0),group='optics')
cyl('EFW cover',(0.055,-.498,Z+.12),.176,.008,black,axis=(0,1,0),group='optics')
for i in range(7):
 a=i*math.tau/7;cyl('EFW cover bolt',(.055+.162*math.sin(a),-.504,Z+.12+.162*math.cos(a)),.006,.005,silver,axis=(0,1,0),group='optics',vertices=12,bevel=0)
cyl('Camera spacer',(0,-.525,Z+.12),.079,.045,silver,axis=(0,1,0),group='optics')
cyl('ASI6200MM Pro red body',(0,-.635,Z+.12),.12,.18,red,axis=(0,1,0),group='optics')
cyl('Camera rear plate',(0,-.733,Z+.12),.113,.018,black,axis=(0,1,0),group='optics')
cyl('Rear fan inlet',(0,-.745,Z+.12),.072,.006,black,axis=(0,1,0),group='optics')
for x in [-.052,-.026,0,.026,.052]:box('Cooling fan grille',(x,-.751,Z+.12),(.006,.006,.125 if abs(x)<.04 else .09),silver,'optics',.001)
for x in [-.084,.084]:cyl('Camera screw',(x,-.748,Z+.12),.006,.006,silver,axis=(0,1,0),group='optics',vertices=12,bevel=0)
box('Camera USB port',(.062,-.752,Z+.045),(.035,.018,.022),silver,'optics',.002)
# Etched branding along the tube side and dew shield.
label('REDCAT 91',(.131,.08,Z+.13),.040,white,rotation=(math.pi/2,0,math.pi/2))
label('WILLIAM OPTICS',(.155,.36,Z+.12),.021,white,rotation=(math.pi/2,0,math.pi/2))
# Export evaluated, triangulated, normal-preserving geometry grouped by part/material.
bpy.context.view_layer.update();deps=bpy.context.evaluated_depsgraph_get();groups={};names=[]
for o in objects:
 ev=o.evaluated_get(deps);mesh=ev.to_mesh();mesh.calc_loop_triangles();normalmatrix=o.matrix_world.to_3x3().inverted().transposed();group=o['rig_group'];name=o.data.materials[0].name;key=(group,name)
 entry=groups.setdefault(key,{'vertices':[],'indices':[],'lookup':{}});names.append(o.name)
 for tri in mesh.loop_triangles:
  for li in tri.loops:
   loop=mesh.loops[li];v=o.matrix_world@mesh.vertices[loop.vertex_index].co;n=(normalmatrix@mesh.corner_normals[li].vector).normalized()
   if group=='optics':v.z-=Z
   values=tuple(round(t,6) for t in (v.x,v.z,-v.y,n.x,n.z,-n.y))
   if values not in entry['lookup']:entry['lookup'][values]=len(entry['vertices'])//6;entry['vertices'].extend(values)
   entry['indices'].append(entry['lookup'][values])
 ev.to_mesh_clear()
out={'name':'Starfront RedCat91 / AM5N','pivotHeight':Z,'source':'scripts/build-mount-model.py (Blender)','parts':names,'materials':materials,'meshes':[]}
for (group,material),e in groups.items():
 out['meshes'].append({'group':group,'material':material,'vertices':base64.b64encode(struct.pack('<%sf'%len(e['vertices']),*e['vertices'])).decode(),'indices':base64.b64encode(struct.pack('<%sI'%len(e['indices']),*e['indices'])).decode()})
path=Path(sys.argv[sys.argv.index('--')+1]);path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(out,separators=(',',':')))
bpy.ops.wm.save_as_mainfile(filepath='/tmp/starfront-rig-model.blend')
print(json.dumps({'output':str(path),'bytes':path.stat().st_size,'parts':len(names),'triangles':sum(len(e['indices'])//3 for e in groups.values())}))
