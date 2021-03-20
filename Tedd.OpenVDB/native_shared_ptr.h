#include "pch.h"
#pragma once
#pragma managed(push, on)
#define FASTCALL [System::Runtime::CompilerServices::MethodImpl(System::Runtime::CompilerServices::MethodImplOptions::AggressiveInlining)]

namespace Tedd {
	namespace OpenVDB {

#pragma once

#include <memory>

		template <class T>
		public ref class native_shared_ptr sealed
		{
			std::shared_ptr<T>* pPtr;

		public:
			FASTCALL
			native_shared_ptr()
				: pPtr(new std::shared_ptr<T>())
			{}

			FASTCALL
			native_shared_ptr(T* t) {
				pPtr = new std::shared_ptr<T>(t);
			}

			FASTCALL
			native_shared_ptr(std::shared_ptr<T> t) {
				pPtr = new std::shared_ptr<T>(t);
			}

			FASTCALL
			native_shared_ptr(const native_shared_ptr<T>% t) {
				pPtr = new std::shared_ptr<T>(*t.pPtr);
			}

			!native_shared_ptr() {
				delete pPtr;
			}

			~native_shared_ptr() {
				delete pPtr;
			}

			FASTCALL
			operator std::shared_ptr<T>() {
				return *pPtr;
			}

			FASTCALL
			native_shared_ptr<T>% operator=(T* ptr) {
				delete pPtr;
				pPtr = new std::shared_ptr<T>(ptr);
				return *this;
			}

			FASTCALL
			T* operator->() {
				return (*pPtr).get();
			}

			FASTCALL
			void reset() {
				pPtr->reset();
			}

			FASTCALL
			void operator=(const native_shared_ptr<T>% rhs)
			{
				*pPtr = *rhs.pPtr;
			}
			
			FASTCALL
			void operator=(const std::shared_ptr<T>% rhs)
			{
				*pPtr = rhs;
			}

			FASTCALL
			std::shared_ptr<T> getSharedPtr()
			{
				return *pPtr;
			}
		};

		//template <class T>
		//public ref class native_shared_ptr sealed
		//{
		//public:
		//    native_shared_ptr()
		//    {
		//        _spNative = new std::shared_ptr<T>;
		//    }
		//    //native_shared_ptr(T* o)
		//    //{
		//    //    _spNative = new std::shared_ptr<T>(o);
		//    //}
		//    native_shared_ptr(const native_shared_ptr<T>% rhs)
		//    {
		//        _spNative = new std::shared_ptr<T>;
		//        *this = rhs;
		//    }
		//    native_shared_ptr(const std::shared_ptr<T>% rhs)
		//    {
		//        _spNative = new std::shared_ptr<T>(rhs);
		//    }
		//    ~native_shared_ptr()
		//    {
		//        reset();
		//    }
		//    void reset()
		//    {
		//        delete _spNative;
		//        _spNative = nullptr;
		//    }
		//    T* operator->()
		//    {
		//        return _spNative->get();
		//    }
		//    operator bool()
		//    {
		//        return _spNative && *_spNative;
		//    }
		//    void operator=(const native_shared_ptr<T>% rhs)
		//    {
		//        *_spNative = *rhs._spNative;
		//    }
		//    void operator=(const std::shared_ptr<T>% rhs)
		//    {
		//        *_spNative = rhs;
		//    }
		//protected:
		//    !native_shared_ptr()
		//    {
		//        reset();
		//    }
		//private:
		//    std::shared_ptr<T>* _spNative;
		//};
	}
}
#pragma managed(pop)