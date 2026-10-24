"""Isolated local OCIO worker. JSON control and straight-alpha float32 pixel files."""
import json
import os
import sys
import numpy as np
import PyOpenColorIO as ocio

ocio.SetLoggingLevel(ocio.LOGGING_LEVEL_NONE)

def config_for(path):
    if path.startswith('ocio://'):
        return ocio.Config.CreateFromBuiltinConfig(path[7:])
    config = ocio.Config.CreateFromFile(os.path.abspath(path))
    config.validate()
    return config

def execute(request):
    config = config_for(request['config'])
    if request['action'] == 'inspect':
        return dict(version=ocio.__version__, description=config.getDescription(),
            roles=[dict(name=role, space=space) for role, space in config.getRoles()],
            spaces=[dict(name=s.getName(), family=s.getFamily(), encoding=s.getEncoding(), description=s.getDescription()) for s in config.getColorSpaces()],
            displays=[dict(name=d, views=list(config.getViews(d))) for d in config.getDisplays()],
            builtins=[dict(name=b[0], description=b[1]) for b in ocio.BuiltinConfigRegistry().getBuiltinConfigs()])
    source, target = request['source'], request['target']
    if request.get('display_view', False):
        display = 'sRGB - Display'
        if display not in list(config.getDisplays()):
            raise ValueError('Scene compensation requires an sRGB - Display in this configuration.')
        view = config.getDefaultView(display)
        processor = config.getProcessor(ocio.DisplayViewTransform(src=source, display=display, view=view)).getDefaultCPUProcessor()
    else:
        processor = config.getProcessor(source, target).getDefaultCPUProcessor()
    if request['action'] == 'validate':
        return dict(identity=processor.isIdentity())
    count = request['width'] * request['height']
    if count < 1 or count > 100_000_000 or os.path.getsize(request['input']) != count * 16:
        raise ValueError('Invalid OpenColorIO pixel buffer.')
    with open(request['input'], 'rb') as src, open(request['output'], 'wb') as dest:
        left = count
        while left:
            length = min(left, 262144)
            pixels = np.fromfile(src, dtype='<f4', count=length * 4).reshape((length, 4))
            alpha = pixels[:, 3].copy()
            processor.applyRGBA(pixels)
            pixels[:, 3] = alpha
            if not np.isfinite(pixels).all():
                raise ValueError('OpenColorIO produced non-finite pixels.')
            pixels.tofile(dest)
            left -= length
    return dict(pixels=count)

try:
    request = json.loads(sys.stdin.read(1_048_576))
    print(json.dumps(execute(request), ensure_ascii=True))
except Exception as error:
    print(str(error)[:4096], file=sys.stderr)
    sys.exit(1)
