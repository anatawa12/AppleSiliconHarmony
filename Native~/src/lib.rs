#![no_std]

use core::ffi::{c_int, c_void};

// /usr/lib/libSystem.B.dylib
#[link(name = "System.B", kind = "dylib")]
unsafe extern "C" {
    fn pthread_jit_write_protect_np(enabled: c_int);
    fn sys_icache_invalidate(start: *mut c_void, len: c_int) -> c_int;
}

#[unsafe(no_mangle)]
pub extern "C" fn redirect_and_clear_cache(
    ptr: *mut c_void,
    destination: *mut c_void,
) {
    let destination_addr = destination.addr() as u64;

    // We emit code like this:
    //  ldr x15, #8 ; 4F 00 00 58  0x5800004F
    //  br  x15     ; E0 01 1F D6  0xD61F01E0
    //  ; TargetAddressHereWith8bytes

    unsafe {
        pthread_jit_write_protect_np(0);

        core::ptr::write_unaligned::<u32>(
            ptr.byte_offset(0) as _,
            0x5800004F
        );
        core::ptr::write_unaligned::<u32>(
            ptr.byte_offset(4) as _,
            0xD61F01E0
        );
        core::ptr::write_unaligned::<u64>(
            ptr.byte_offset(8) as _,
            destination_addr
        );

        sys_icache_invalidate(ptr, 16);

        pthread_jit_write_protect_np(1);
    }
}

#[panic_handler]
fn dummy_panic(_info: &core::panic::PanicInfo) -> ! {
    loop {}
}
