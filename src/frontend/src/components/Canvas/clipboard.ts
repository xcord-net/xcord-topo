import type { Container, Image, Wire as WireType } from '../../types/topology';

/** Deep-clone a container tree, generating fresh IDs. Returns the clone and an old->new ID map. */
export function cloneContainer(c: Container): { clone: Container; idMap: Map<string, string> } {
  const idMap = new Map<string, string>();
  const cloneC = (src: Container): Container => {
    const newId = crypto.randomUUID();
    idMap.set(src.id, newId);
    return {
      ...src,
      id: newId,
      ports: src.ports.map(p => {
        const newPid = crypto.randomUUID();
        idMap.set(p.id, newPid);
        return { ...p, id: newPid };
      }),
      images: src.images.map(img => {
        const newImgId = crypto.randomUUID();
        idMap.set(img.id, newImgId);
        return {
          ...img,
          id: newImgId,
          ports: img.ports.map(p => {
            const newPid = crypto.randomUUID();
            idMap.set(p.id, newPid);
            return { ...p, id: newPid };
          }),
          config: { ...img.config },
        };
      }),
      children: src.children.map(cloneC),
      config: { ...src.config },
    };
  };
  return { clone: cloneC(c), idMap };
}

/** Deep-clone an image, generating fresh IDs. */
export function cloneImage(img: Image): Image {
  return {
    ...img,
    id: crypto.randomUUID(),
    ports: img.ports.map(p => ({ ...p, id: crypto.randomUUID() })),
    config: { ...img.config },
  };
}

export type Clipboard =
  | { type: 'container'; data: Container; wires: WireType[] }
  | { type: 'image'; data: Image; parentId: string };
