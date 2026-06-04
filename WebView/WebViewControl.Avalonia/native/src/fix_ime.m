#import <Foundation/Foundation.h>
#import <AppKit/AppKit.h>
#import <objc/runtime.h>

typedef NSAttributedString* (*OrigIMP)(id self, SEL _cmd, NSRange range, NSRangePointer actualRange);

static OrigIMP _orig_attributedSubstringForProposedRange = NULL;
static BOOL _isFixed = NO;

static NSAttributedString* safe_attributedSubstringForProposedRange(id self, SEL _cmd, NSRange range, NSRangePointer actualRange) {
    if (actualRange) {
        range = *actualRange;
    }
    
    // Get the _text ivar (NSMutableAttributedString)
    Ivar textIvar = class_getInstanceVariable([self class], "_text");
    if (!textIvar) {
        return [[NSAttributedString alloc] initWithString:@""];
    }
    
    NSMutableAttributedString* text = object_getIvar(self, textIvar);
    if (!text) {
        return [[NSAttributedString alloc] initWithString:@""];
    }
    
    NSUInteger textLength = [text length];
    
    // Clamp range to valid bounds
    if (range.location > textLength) {
        if (actualRange) {
            *actualRange = NSMakeRange(textLength, 0);
        }
        return [[NSAttributedString alloc] initWithString:@""];
    }
    
    NSUInteger available = textLength - range.location;
    if (range.length > available) {
        range.length = available;
    }
    
    if (actualRange) {
        *actualRange = range;
    }
    
    // Call original (which is now swapped to this function's slot via method_setImplementation)
    return _orig_attributedSubstringForProposedRange(self, _cmd, range, actualRange);
}

// Exported function to be called from C#
__attribute__((visibility("default")))
int apply_avnview_ime_fix(void) {
    if (_isFixed) return 1;
    
    Class avnViewClass = objc_getClass("AvnView");
    if (!avnViewClass) {
        NSLog(@"[IME-Fix] AvnView class not found");
        return 0;
    }
    
    SEL sel = sel_registerName("attributedSubstringForProposedRange:actualRange:");
    Method method = class_getInstanceMethod(avnViewClass, sel);
    if (!method) {
        NSLog(@"[IME-Fix] Method not found on AvnView");
        return 0;
    }
    
    _orig_attributedSubstringForProposedRange = (OrigIMP)method_setImplementation(method, (IMP)safe_attributedSubstringForProposedRange);
    _isFixed = YES;
    NSLog(@"[IME-Fix] AvnView.attributedSubstringForProposedRange:actualRange: patched successfully");
    return 1;
}
