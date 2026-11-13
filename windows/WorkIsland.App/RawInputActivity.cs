using System.Buffers.Binary;

namespace WorkIsland.App;

internal enum RawActivity { None, Pointer, Keyboard }
internal static class RawInputActivity
{
    // Mac observes keyDown + flagsChanged and pointer motion/down/scroll,
    // rather than treating an ordinary key/button release as new activity.
    internal static RawActivity Classify(uint type,byte[] packet,int offset,int length,bool absoluteMoved)
    {
        if(offset<0 || length<0 || offset>packet.Length || length>packet.Length-offset)return RawActivity.None;
        var data=packet.AsSpan(offset,length);
        if(type==1 && length>=16)
        {
            var flags=BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            // Inspect the modifier category transiently; never retain key values.
            var key=BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
            if(key is 0 or >=255 || (flags&~7)!=0)return RawActivity.None;
            var modifier=key is 0x10 or 0x11 or 0x12 or 0x5b or 0x5c or >=0xa0 and <=0xa5;
            return (flags&1)==0 || modifier?RawActivity.Keyboard:RawActivity.None;
        }
        if(type==0 && length>=24)
        {
            var flags=BinaryPrimitives.ReadUInt16LittleEndian(data);
            var buttons=BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            var moved=(flags&1)!=0?absoluteMoved:
                BinaryPrimitives.ReadInt32LittleEndian(data[12..])!=0 || BinaryPrimitives.ReadInt32LittleEndian(data[16..])!=0;
            return moved || (buttons&0xd55)!=0?RawActivity.Pointer:RawActivity.None;
        }
        return RawActivity.None;
    }
}
