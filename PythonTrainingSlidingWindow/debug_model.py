import json
import numpy as np

d = json.load(open('../Assets/scaler_sliding.json'))
print(f"mean len: {len(d['mean'])}")
print(f"std len: {len(d['std'])}")
print(f"mean[:5]: {d['mean'][:5]}")
print(f"std[:5]: {d['std'][:5]}")

# Verificar si hay std=0 (problematico)
std = np.array(d['std'])
print(f"std==0 count: {(std == 0).sum()}")
print(f"std min: {std.min()}, std max: {std.max()}")

# Test del modelo ONNX con inputs variados
try:
    import onnxruntime as ort
    sess = ort.InferenceSession('../Assets/SoccerModel_Sliding.onnx')
    print(f"\nONNX inputs: {[(i.name, i.shape) for i in sess.get_inputs()]}")
    print(f"ONNX outputs: {[(o.name, o.shape) for o in sess.get_outputs()]}")
    
    # Test con zeros
    zeros = np.zeros((1, 80), dtype=np.float32)
    r = sess.run(None, {'vector_observation': zeros})
    print(f"\nInput=zeros -> mov shape: {r[0].shape}, argmax: {np.argmax(r[0])}, logits: {r[0]}")
    
    # Test con random
    for i in range(5):
        rand = np.random.randn(1, 80).astype(np.float32)
        r = sess.run(None, {'vector_observation': rand})
        print(f"Input=random{i} -> argmax: {np.argmax(r[0])}, max_logit: {r[0].max():.2f}")
except ImportError:
    print("onnxruntime no instalado, saltando verificacion ONNX")
